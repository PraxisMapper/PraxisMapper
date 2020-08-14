#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:5.0-buster-slim AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# create sql server install?
from mcr.microsoft.com/mssql/server:2019-CU5-ubuntu-18.04 as sql


FROM mcr.microsoft.com/dotnet/sdk:5.0-buster-slim AS build
WORKDIR /src
COPY ["GPSExploreServerAPI/GPSExploreServerAPI.csproj", "GPSExploreServerAPI/"]
RUN dotnet restore "GPSExploreServerAPI/GPSExploreServerAPI.csproj"
COPY . .
WORKDIR "/src/GPSExploreServerAPI"
RUN dotnet build "GPSExploreServerAPI.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "GPSExploreServerAPI.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GPSExploreServerAPI.dll"]