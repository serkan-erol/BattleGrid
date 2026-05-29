FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:10000
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 10000

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["BattleGrid.API/BattleGrid.API.csproj",                         "BattleGrid.API/"]
COPY ["BattleGrid.Application/BattleGrid.Application.csproj",         "BattleGrid.Application/"]
COPY ["BattleGrid.Contracts/BattleGrid.Contracts.csproj",             "BattleGrid.Contracts/"]
COPY ["BattleGrid.Domain/BattleGrid.Domain.csproj",                   "BattleGrid.Domain/"]
COPY ["BattleGrid.Infrastructure/BattleGrid.Infrastructure.csproj",   "BattleGrid.Infrastructure/"]

RUN dotnet restore "BattleGrid.API/BattleGrid.API.csproj"

COPY . .
#WORKDIR "/src/BattleGrid.API"
RUN dotnet publish "BattleGrid.API/BattleGrid.API.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BattleGrid.API.dll"]