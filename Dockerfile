FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/ src/
RUN dotnet publish src/Nudge.Web \
    --configuration Release \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=litestream/litestream:0.3.13 \
    /usr/local/bin/litestream \
    /usr/local/bin/litestream
COPY litestream.yml /etc/litestream.yml
COPY run.sh /app/run.sh
COPY --from=build /app .
RUN chmod +x /app/run.sh && mkdir -p /data
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ConnectionStrings__Nudge="Data Source=/data/nudge.db"
EXPOSE 8080
ENTRYPOINT ["/app/run.sh"]
