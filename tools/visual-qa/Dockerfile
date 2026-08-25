FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Workslip.VisualQa.csproj .
RUN dotnet restore Workslip.VisualQa.csproj
COPY Program.cs .
RUN dotnet publish Workslip.VisualQa.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Workslip.VisualQa.dll"]
