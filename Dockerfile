FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080

USER app

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG configuration=Release
WORKDIR /src
COPY ["EmailMarketingService.csproj", "./"]
RUN dotnet restore "EmailMarketingService.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "EmailMarketingService.csproj" -c $configuration -o /app/build

FROM build AS publish
ARG configuration=Release
RUN dotnet publish "EmailMarketingService.csproj" -c $configuration -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Временно переключаемся на root, чтобы выдать права
USER root
RUN mkdir -p /app/data /app/uploads && chmod -R 777 /app/data /app/uploads

# Возвращаем пользователя app
USER app

ENTRYPOINT ["dotnet", "EmailMarketingService.dll"]
