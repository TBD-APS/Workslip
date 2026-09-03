FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/BE/WorkslipApi/ ./src/BE/WorkslipApi/
RUN dotnet restore src/BE/WorkslipApi/Workslip.Api.csproj
RUN dotnet publish src/BE/WorkslipApi/Workslip.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out/ ./
ENV ASPNETCORE_HTTP_PORTS=5262 \
    DOTNET_EnableDiagnostics=0
EXPOSE 5262
USER $APP_UID
ENTRYPOINT ["dotnet", "Workslip.Api.dll"]
