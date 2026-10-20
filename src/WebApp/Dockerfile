# Build from the solution root: docker build -f src/WebApp/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS build
WORKDIR /source
COPY global.json .
COPY src/WebApp/WebApp.csproj src/WebApp/
RUN dotnet restore src/WebApp/WebApp.csproj
COPY src/WebApp/ src/WebApp/
RUN dotnet publish src/WebApp/WebApp.csproj -c Release --no-restore -o /out /p:UseAppHost=false

# The SDK stays in the build stage; the final image needs only ASP.NET Core.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble AS runtime
WORKDIR /app
# WebApp serves static assets and owns no database.
COPY --from=build /out .
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ASPNETCORE_ENVIRONMENT=Docker
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "WebApp.dll"]
