FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
# Note: Falling back to 8.0/9.0 if 10.0 image is not available locally, but we will use the matching SDK for the project if possible.
# Wait, let's use the standard multi-stage build.
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /app

# Copy csproj and restore as distinct layers
COPY ["SafeRelease.csproj", "./"]
RUN dotnet restore "SafeRelease.csproj"

# Copy everything else and build
COPY . .
RUN dotnet publish "SafeRelease.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 5258
ENV ASPNETCORE_URLS=http://+:5258

ENTRYPOINT ["dotnet", "SafeRelease.dll"]
