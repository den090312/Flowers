FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Order/Order.csproj", "Order/"]
RUN dotnet restore "Order/Order.csproj"
COPY . .
RUN dotnet publish "Order/Order.csproj" -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Order.dll"]