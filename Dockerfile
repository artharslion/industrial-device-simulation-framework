FROM node:22-alpine AS client-build
WORKDIR /src/src/IndustrialSim.Web/ClientApp
COPY src/IndustrialSim.Web/ClientApp/package.json src/IndustrialSim.Web/ClientApp/package-lock.json ./
RUN npm ci
COPY src/IndustrialSim.Web/ClientApp/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
COPY --from=client-build /src/src/IndustrialSim.Web/wwwroot ./src/IndustrialSim.Web/wwwroot
RUN dotnet restore IndustrialSim.sln
RUN dotnet publish src/IndustrialSim.Web/IndustrialSim.Web.csproj --configuration Release --no-restore --output /out -p:SkipClientBuild=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out .
COPY examples/devices/pump.yaml /app/config/device.yaml
COPY examples/scenarios /app/config/scenarios
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV INDUSTRIALSIM_DEVICE_CONFIG=/app/config/device.yaml
EXPOSE 4840 5020 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "IndustrialSim.Web.dll"]
