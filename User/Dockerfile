FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["User/User.csproj", "User/"]
RUN dotnet restore "User/User.csproj"
COPY . .
RUN dotnet publish "User/User.csproj" -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "User.dll"]