FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first, with just the project files, so dependency layers cache
# independently of application code changes.
COPY KayeDM.BMS/KayeDM.BMS.slnx KayeDM.BMS/
COPY KayeDM.BMS/src/KayeDM.Domain/KayeDM.Domain.csproj KayeDM.BMS/src/KayeDM.Domain/
COPY KayeDM.BMS/src/KayeDM.Application/KayeDM.Application.csproj KayeDM.BMS/src/KayeDM.Application/
COPY KayeDM.BMS/src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj KayeDM.BMS/src/KayeDM.Infrastructure/
COPY KayeDM.BMS/src/KayeDM.Web/KayeDM.Web.csproj KayeDM.BMS/src/KayeDM.Web/
COPY KayeDM.BMS/tests/KayeDM.Tests/KayeDM.Tests.csproj KayeDM.BMS/tests/KayeDM.Tests/
RUN dotnet restore KayeDM.BMS/src/KayeDM.Web/KayeDM.Web.csproj

COPY KayeDM.BMS/ KayeDM.BMS/
RUN dotnet publish KayeDM.BMS/src/KayeDM.Web/KayeDM.Web.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "KayeDM.Web.dll"]
