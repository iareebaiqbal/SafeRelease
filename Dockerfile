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
