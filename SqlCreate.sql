/* ========= Create DB (idempotent) ========= */
DECLARE @db sysname = N'ClinicalCoding';
IF DB_ID(@db) IS NULL
BEGIN
  EXEC('CREATE DATABASE [' + @db + ']');
END
GO
USE [ClinicalCoding];
GO

/* ========= Episodes ========= */
IF OBJECT_ID('dbo.Episodes','U') IS NULL
BEGIN
  CREATE TABLE dbo.Episodes
  (
    Id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Episodes PRIMARY KEY,
    NHSNumber        NVARCHAR(20)     NULL,
    PatientName      NVARCHAR(200)    NULL,
    AdmissionDate    DATETIME2(0)     NOT NULL,
    DischargeDate    DATETIME2(0)     NULL,
    Specialty        NVARCHAR(100)    NULL,
    SourceText       NVARCHAR(MAX)    NULL,
    Status           INT              NOT NULL CONSTRAINT DF_Episodes_Status DEFAULT(0), -- 0=Draft,1=Submitted,2=Approved,3=Rejected (example)
    CreatedOn        DATETIME2(0)     NOT NULL CONSTRAINT DF_Episodes_CreatedOn DEFAULT (SYSUTCDATETIME()),
    CreatedBy        NVARCHAR(256)    NULL,
    SubmittedOn      DATETIME2(0)     NULL,
    SubmittedBy      NVARCHAR(256)    NULL,
    ApprovedOn       DATETIME2(0)     NULL,
    ApprovedBy       NVARCHAR(256)    NULL,
    RejectedOn       DATETIME2(0)     NULL,
    RejectedBy       NVARCHAR(256)    NULL,
    Notes            NVARCHAR(MAX)    NULL
  );
  CREATE INDEX IX_Episodes_Status ON dbo.Episodes(Status);
  CREATE INDEX IX_Episodes_Admission ON dbo.Episodes(AdmissionDate);
END
GO

/* ========= Diagnoses ========= */
IF OBJECT_ID('dbo.Diagnoses','U') IS NULL
BEGIN
  CREATE TABLE dbo.Diagnoses
  (
    Id           UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Diagnoses PRIMARY KEY,
    EpisodeId    UNIQUEIDENTIFIER NOT NULL,
    Code         NVARCHAR(16)     NOT NULL,
    Description  NVARCHAR(512)    NULL,
    IsPrimary    BIT              NOT NULL CONSTRAINT DF_Diagnoses_IsPrimary DEFAULT(0),

    CONSTRAINT FK_Diagnoses_Episodes
      FOREIGN KEY(EpisodeId) REFERENCES dbo.Episodes(Id) ON DELETE CASCADE
  );
  CREATE INDEX IX_Diagnoses_Episode ON dbo.Diagnoses(EpisodeId);
  CREATE INDEX IX_Diagnoses_Code ON dbo.Diagnoses(Code);
  CREATE UNIQUE INDEX UX_Diagnoses_PrimaryPerEpisode
    ON dbo.Diagnoses(EpisodeId, IsPrimary DESC, Code);
END
GO

/* ========= Procedures ========= */
IF OBJECT_ID('dbo.Procedures','U') IS NULL
BEGIN
  CREATE TABLE dbo.Procedures
  (
    Id           UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Procedures PRIMARY KEY,
    EpisodeId    UNIQUEIDENTIFIER NOT NULL,
    Code         NVARCHAR(16)     NOT NULL,
    Description  NVARCHAR(512)    NULL,
    PerformedOn  DATETIME2(0)     NULL,

    CONSTRAINT FK_Procedures_Episodes
      FOREIGN KEY(EpisodeId) REFERENCES dbo.Episodes(Id) ON DELETE CASCADE
  );
  CREATE INDEX IX_Procedures_Episode ON dbo.Procedures(EpisodeId);
  CREATE INDEX IX_Procedures_Code ON dbo.Procedures(Code);
END
GO

/* ========= ClinicianQueries ========= */
IF OBJECT_ID('dbo.ClinicianQueries','U') IS NULL
BEGIN
  CREATE TABLE dbo.ClinicianQueries
  (
    Id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ClinicianQueries PRIMARY KEY,
    EpisodeId         UNIQUEIDENTIFIER NOT NULL,
    ToClinician       NVARCHAR(256)    NULL,
    Subject           NVARCHAR(200)    NULL,
    Body              NVARCHAR(MAX)    NULL,
    CreatedBy         NVARCHAR(256)    NULL,
    CreatedOn         DATETIME2(0)     NOT NULL CONSTRAINT DF_ClinicianQueries_CreatedOn DEFAULT (SYSUTCDATETIME()),
    ExternalReference NVARCHAR(256)    NULL,
    RespondedBy       NVARCHAR(256)    NULL,
    RespondedOn       DATETIME2(0)     NULL,
    ResponseText      NVARCHAR(MAX)    NULL,

    CONSTRAINT FK_ClinicianQueries_Episodes
      FOREIGN KEY(EpisodeId) REFERENCES dbo.Episodes(Id) ON DELETE CASCADE
  );
  CREATE INDEX IX_ClinicianQueries_Episode ON dbo.ClinicianQueries(EpisodeId);
  CREATE INDEX IX_ClinicianQueries_CreatedOn ON dbo.ClinicianQueries(CreatedOn);
END
GO

/* ========= AuditEntries ========= */
IF OBJECT_ID('dbo.AuditEntries','U') IS NULL
BEGIN
  CREATE TABLE dbo.AuditEntries
  (
    Id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_AuditEntries PRIMARY KEY,
    Timestamp   DATETIME2(0)     NOT NULL CONSTRAINT DF_Audit_Timestamp DEFAULT (SYSUTCDATETIME()),
    PerformedBy NVARCHAR(256)    NULL,
    Action      NVARCHAR(128)    NOT NULL,
    EntityType  NVARCHAR(128)    NULL,
    EntityId    NVARCHAR(128)    NULL,
    PayloadJson NVARCHAR(MAX)    NULL
  );
  CREATE INDEX IX_AuditEntries_Timestamp ON dbo.AuditEntries(Timestamp DESC);
  CREATE INDEX IX_AuditEntries_Entity ON dbo.AuditEntries(EntityType, EntityId, Timestamp DESC);
END
GO

/* ========= DeadLetters ========= */
IF OBJECT_ID('dbo.DeadLetters','U') IS NULL
BEGIN
  CREATE TABLE dbo.DeadLetters
  (
    Id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_DeadLetters PRIMARY KEY,
    Kind        NVARCHAR(128)    NOT NULL,
    PayloadJson NVARCHAR(MAX)    NOT NULL,
    Error       NVARCHAR(1024)   NULL,
    Attempts    INT              NOT NULL CONSTRAINT DF_DeadLetters_Attempts DEFAULT(0),
    CreatedOn   DATETIME2(0)     NOT NULL CONSTRAINT DF_DeadLetters_CreatedOn DEFAULT (SYSUTCDATETIME()),
    LastTriedOn DATETIME2(0)     NULL
  );
  CREATE INDEX IX_DeadLetters_CreatedOn ON dbo.DeadLetters(CreatedOn DESC);
END
GO

/* ========= RevertRequests (two-step approval) ========= */
IF OBJECT_ID('dbo.RevertRequests','U') IS NULL
BEGIN
  CREATE TABLE dbo.RevertRequests
  (
    Id           UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_RevertRequests PRIMARY KEY,
    EpisodeId    UNIQUEIDENTIFIER NOT NULL,
    AuditId      UNIQUEIDENTIFIER NOT NULL,
    RequestedBy  NVARCHAR(256)    NOT NULL,
    RequestedOn  DATETIME2(0)     NOT NULL CONSTRAINT DF_RevertRequests_RequestedOn DEFAULT (SYSUTCDATETIME()),
    ApprovedBy   NVARCHAR(256)    NULL,
    ApprovedOn   DATETIME2(0)     NULL,
    RejectedBy   NVARCHAR(256)    NULL,
    RejectedOn   DATETIME2(0)     NULL,
    Status       INT              NOT NULL CONSTRAINT DF_RevertRequests_Status DEFAULT(0), -- 0=Pending,1=Approved,2=Rejected

    CONSTRAINT FK_RevertRequests_Episodes
      FOREIGN KEY(EpisodeId) REFERENCES dbo.Episodes(Id) ON DELETE CASCADE
  );
  CREATE INDEX IX_RevertRequests_Episode_Status ON dbo.RevertRequests(EpisodeId, Status);
END
GO

/* ========= Optional: simple constraints ========= */
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Diagnoses_Code_NotEmpty')
  ALTER TABLE dbo.Diagnoses
    ADD CONSTRAINT CK_Diagnoses_Code_NotEmpty CHECK (LEN(LTRIM(RTRIM(Code))) > 0);

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Procedures_Code_NotEmpty')
  ALTER TABLE dbo.Procedures
    ADD CONSTRAINT CK_Procedures_Code_NotEmpty CHECK (LEN(LTRIM(RTRIM(Code))) > 0);

PRINT 'ClinicalCoding DB schema is ready.';
