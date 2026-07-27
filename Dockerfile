FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy csproj and restore as distinct layers
COPY ["SafeRelease.csproj", "./"]
RUN dotnet restore "SafeRelease.csproj"

# Copy everything else and build
COPY . .
RUN dotnet publish "SafeRelease.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

# Install ffmpeg so Xabe.FFmpeg can use the system binary (no runtime download needed)
RUN apt-get update && apt-get install -y ffmpeg && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

EXPOSE 5258
ENV ASPNETCORE_URLS=http://+:5258

ENTRYPOINT ["dotnet", "SafeRelease.dll"]
