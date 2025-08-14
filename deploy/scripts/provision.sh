
#!/usr/bin/env bash
set -euo pipefail

# REQUIRED: set these before running
RG=${RG:-clinicalcoding-rg}
LOC=${LOC:-uksouth}
SQL_SERVER=${SQL_SERVER:-clinicalcoding-sql$RANDOM}
SQL_ADMIN=${SQL_ADMIN:-sqladminuser}
SQL_PWD=${SQL_PWD:-"YourStrong(!)Password1"}
APPPLAN=${APPPLAN:-clinicalcoding-plan}
API_APP=${API_APP:-clinicalcoding-api}
STORAGE=${STORAGE:-clinicalcodingweb$RANDOM}

echo "Creating resource group $RG in $LOC"
az group create -n "$RG" -l "$LOC"

echo "Creating Azure SQL server + DB"
az sql server create -g "$RG" -n "$SQL_SERVER" -l "$LOC" -u "$SQL_ADMIN" -p "$SQL_PWD"
az sql db create -g "$RG" -s "$SQL_SERVER" -n ClinicalCoding --service-objective S0
MYIP=$(curl -s ifconfig.me || echo "0.0.0.0")
az sql server firewall-rule create -g "$RG" -s "$SQL_SERVER" -n clientip --start-ip-address "$MYIP" --end-ip-address "$MYIP"

echo "App Service plan + Web App (API)"
az appservice plan create -g "$RG" -n "$APPPLAN" --sku P1v3 --is-linux
az webapp create -g "$RG" -p "$APPPLAN" -n "$API_APP" --runtime "DOTNET:9.0"

echo "Storage static website for SPA"
az storage account create -g "$RG" -n "$STORAGE" -l "$LOC" --sku Standard_LRS
az storage blob service-properties update --account-name "$STORAGE" --static-website --index-document index.html --404-document index.html

echo "Connection string"
CONN="Server=tcp:$SQL_SERVER.database.windows.net,1433;Initial Catalog=ClinicalCoding;Persist Security Info=False;User ID=$SQL_ADMIN;Password=$SQL_PWD;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
az webapp config connection-string set -g "$RG" -n "$API_APP" -t SQLAzure --settings DefaultConnection="$CONN"

echo "Done. Deploy API to $API_APP and upload SPA dist/ to the storage static website."
