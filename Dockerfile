FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore IndustrialSim.sln
RUN dotnet publish src/IndustrialSim.Web/IndustrialSim.Web.csproj --configuration Release --no-restore --output /out

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
