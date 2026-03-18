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
COPY openssl-legacy.cnf .
COPY render-entrypoint.sh .
RUN chmod +x render-entrypoint.sh

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV OPENSSL_CONF=/app/openssl-legacy.cnf
ENTRYPOINT ["./render-entrypoint.sh"]
