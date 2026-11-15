# Wend Your Way in a container (#146, T142, STORY-046): the planner and the `wend` command line in one
# image, with osm2pgsql to load a region's map. compose.yaml runs it beside PostgreSQL with PostGIS.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/WendYourWay.Web -c Release -o /app/web \
 && dotnet publish src/WendYourWay.Cli -c Release -o /app/wend

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update \
 && apt-get install -y --no-install-recommends osm2pgsql \
 && rm -rf /var/lib/apt/lists/*
COPY --from=build /app /app
ENV WEND_OSM2PGSQL=/usr/bin/osm2pgsql
# The planner, bound inside the container; compose.yaml publishes it on this computer only.
WORKDIR /app/web
EXPOSE 5018
ENTRYPOINT ["dotnet", "/app/web/WendYourWay.Web.dll", "--urls", "http://0.0.0.0:5018"]
