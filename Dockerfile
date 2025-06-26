# Use the official .NET SDK image for building and running in dev
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS dev

WORKDIR /app

# Copy csproj and restore as distinct layers
COPY MServer/MServer.csproj ./MServer/
RUN dotnet restore ./MServer/MServer.csproj

# Copy the rest of the source code
COPY MServer/ ./MServer/
COPY MServer/appSettings.json ./MServer/

# Set working directory for the app
WORKDIR /app/MServer

# Expose default Kestrel ports
EXPOSE 5000
EXPOSE 5001

# Set environment for development
ENV ASPNETCORE_ENVIRONMENT=Development

# Use dotnet watch for live reload in development and log output to a file
CMD ["sh", "-c", "dotnet watch run --urls http://0.0.0.0:5000;http://0.0.0.0:5001 2>&1 | tee /app/MServer/server.log"]

COPY aspnetapp.pfx /https/aspnetapp.pfx
