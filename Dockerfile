# Wafek_Web_Manager — للرفع على Render
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY . ./
RUN dotnet publish -c Release -o /app/publish

# مرحلة التشغيل
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .
COPY --from=build /src/SQL ./SQL
# Copy openssl config for legacy server support
COPY openssl-legacy.cnf /etc/ssl/openssl-legacy.cnf

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Configure OpenSSL to allow legacy connections (TLS 1.0/1.1)
ENV OPENSSL_CONF=/etc/ssl/openssl-legacy.cnf

ENTRYPOINT ["dotnet", "Wafek_Web_Manager.dll"]
