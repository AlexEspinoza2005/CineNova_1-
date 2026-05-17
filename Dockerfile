# build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["MovieApi.csproj", "./"]
RUN dotnet restore
COPY . .
RUN dotnet publish "MovieApi.csproj" -c Release -o /app/publish

# final stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Expose the port Render uses
ENV ASPNETCORE_URLS=http://+:${PORT:-8080}

ENTRYPOINT ["dotnet", "MovieApi.dll"]
