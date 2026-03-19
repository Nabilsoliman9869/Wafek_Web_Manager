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

# Patch system OpenSSL to allow TLS 1.0/1.1 for legacy SQL Server compatibility
RUN sed -i 's/MinProtocol = TLSv1.2/MinProtocol = TLSv1/' /etc/ssl/openssl.cnf || true && \
    sed -i 's/CipherString = DEFAULT@SECLEVEL=2/CipherString = DEFAULT@SECLEVEL=0/' /etc/ssl/openssl.cnf || true && \
    echo "[ legacy_sect ]" >> /etc/ssl/openssl.cnf && \
    echo "MinProtocol = TLSv1" >> /etc/ssl/openssl.cnf && \
    echo "CipherString = DEFAULT:@SECLEVEL=0" >> /etc/ssl/openssl.cnf

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "Wafek_Web_Manager.dll"]
