# ETAPA 1: Base (Entorno de ejecución ligero para Render)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# ETAPA 2: Compilación (SDK pesado para el build)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copiamos solo los archivos .csproj primero para aprovechar el caché de Docker
# Si el código fuente cambia pero las dependencias no, Docker reutiliza esta capa
COPY ["MedCareOS.Api/MedCareOS.Api.csproj", "MedCareOS.Api/"]
COPY ["MedCareOS.Application/MedCareOS.Application.csproj", "MedCareOS.Application/"]
COPY ["MedCareOS.Domain/MedCareOS.Domain.csproj", "MedCareOS.Domain/"]
COPY ["MedCareOS.Infrastructure/MedCareOS.Infrastructure.csproj", "MedCareOS.Infrastructure/"]

# Restauramos dependencias
RUN dotnet restore "MedCareOS.Api/MedCareOS.Api.csproj"

# Copiamos el resto del código fuente
COPY . .
WORKDIR "/src/MedCareOS.Api"

# Compilamos en modo Release
RUN dotnet build "MedCareOS.Api.csproj" -c Release -o /app/build

# ETAPA 3: Publicación
FROM build AS publish
RUN dotnet publish "MedCareOS.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ETAPA 4: Contenedor Final
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MedCareOS.Api.dll"]