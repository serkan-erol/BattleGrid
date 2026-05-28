FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:10000
EXPOSE 10000

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["BattleGrid.Web/BattleGrid.Web.csproj",						  "BattleGrid.Web/"]
COPY ["BattleGrid.Contracts/BattleGrid.Contracts.csproj",             "BattleGrid.Contracts/"]
COPY ["BattleGrid.Domain/BattleGrid.Domain.csproj",                   "BattleGrid.Domain/"]

RUN dotnet restore "BattleGrid.Web/BattleGrid.Web.csproj"

COPY . .
#WORKDIR "/src/BattleGrid.Web"
RUN dotnet publish "BattleGrid.Web/BattleGrid.Web.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BattleGrid.Web.dll"]