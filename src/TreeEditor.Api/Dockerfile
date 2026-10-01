FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/TreeEditor.Domain/TreeEditor.Domain.csproj src/TreeEditor.Domain/
COPY src/TreeEditor.Application/TreeEditor.Application.csproj src/TreeEditor.Application/
COPY src/TreeEditor.Infrastructure/TreeEditor.Infrastructure.csproj src/TreeEditor.Infrastructure/
COPY src/TreeEditor.Api/TreeEditor.Api.csproj src/TreeEditor.Api/
RUN dotnet restore src/TreeEditor.Api/TreeEditor.Api.csproj
COPY src/ src/
RUN dotnet publish src/TreeEditor.Api/TreeEditor.Api.csproj --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TreeEditor.Api.dll"]
