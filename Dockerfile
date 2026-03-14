# Use the official .NET SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy everything and restore dependencies
COPY . ./
RUN dotnet restore

# Build the app
RUN dotnet publish -c Release -o out

# Use ASP.NET runtime image to run the app
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/out ./

# Set environment variable so the app listens on port 80
ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80

# Run the app
ENTRYPOINT ["dotnet", "WeatherService.dll"]