#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS base
WORKDIR /app
EXPOSE 5000-5001
EXPOSE 20000-29999

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["Tubumu.Web.csproj", "."]
RUN dotnet restore "Tubumu.Web.csproj"
COPY . .
RUN dotnet build "Tubumu.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Tubumu.Web.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Tubumu.Web.dll"]
