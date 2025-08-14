
# Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ../Directory.Build.props ./
COPY ClinicalCoding.Api/ ClinicalCoding.Api/
COPY ../ClinicalCoding.Domain/ ClinicalCoding.Domain/
COPY ../ClinicalCoding.Infrastructure/ ClinicalCoding.Infrastructure/
RUN dotnet restore ClinicalCoding.Api/ClinicalCoding.Api.csproj
RUN dotnet publish ClinicalCoding.Api/ClinicalCoding.Api.csproj -c Release -o /app/publish

# Run
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "ClinicalCoding.Api.dll"]
