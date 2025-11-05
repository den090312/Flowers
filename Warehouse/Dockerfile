FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Warehouse/Warehouse.csproj", "Warehouse/"]
RUN dotnet restore "Warehouse/Warehouse.csproj"
COPY . .
RUN dotnet publish "Warehouse/Warehouse.csproj" -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Warehouse.dll"]