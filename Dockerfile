# Use the official .NET SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy project files
COPY . ./

# Restore dependencies
RUN dotnet restore

# Build the application
RUN dotnet publish -c Release -o out

# Use the runtime image for the final stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

# Copy the built application
COPY --from=build /app/out .
COPY --from=build .env .

# Create wwwroot directory
RUN mkdir -p wwwroot

# Expose port 5001
EXPOSE 5001

# Set environment variables
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:5001
ENV LANG=C.UTF-8
ENV LC_ALL=C.UTF-8

# Run the application
ENTRYPOINT ["dotnet", "Agg.dll"]