FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ConveyorDashboard/ConveyorDashboard.csproj ConveyorDashboard/
RUN dotnet restore ConveyorDashboard/ConveyorDashboard.csproj

COPY ConveyorDashboard/ ConveyorDashboard/

RUN dotnet publish ConveyorDashboard/ConveyorDashboard.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# ML.NET LightGBM uses OpenMP for CPU training and inference on Linux.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgomp1 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "ConveyorDashboard.dll"]
