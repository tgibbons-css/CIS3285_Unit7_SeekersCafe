# Build from the solution root: docker build -f src/MenuService/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS build
WORKDIR /source
COPY global.json .
COPY src/MenuService/MenuService.csproj src/MenuService/
RUN dotnet restore src/MenuService/MenuService.csproj
COPY src/MenuService/ src/MenuService/
RUN dotnet publish src/MenuService/MenuService.csproj -c Release --no-restore -o /out /p:UseAppHost=false

# The SDK stays in the build stage; the final image needs only ASP.NET Core.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble AS runtime
WORKDIR /app
# A new named volume inherits this directory's non-root ownership.
RUN mkdir /data && chown app:app /data
COPY --from=build /out .
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ASPNETCORE_ENVIRONMENT=Docker
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "MenuService.dll"]
