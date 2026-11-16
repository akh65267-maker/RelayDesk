# Shared Dockerfile for all .NET services. Build with:
#   docker build -f deploy/Dockerfile --build-arg PROJECT=RelayDesk.Api -t relay-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /repo
COPY . .
RUN dotnet publish src/${PROJECT}/${PROJECT}.csproj -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG PROJECT
ENV APP_DLL=${PROJECT}.dll \
    ASPNETCORE_HTTP_PORTS=8080
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet $APP_DLL"]
