#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS base
WORKDIR /app
EXPOSE 5000-5001
EXPOSE 20000-29999

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["Tubumu.Web/Tubumu.Web.csproj", "Tubumu.Web/"]
COPY ["../Tubumu.Core/Tubumu.Core.csproj", "../Tubumu.Core/"]
COPY ["../Tubumu.Mediasoup/Tubumu.Mediasoup.csproj", "../Tubumu.Mediasoup/"]
COPY ["../Tubumu.Mediasoup.Common/Tubumu.Mediasoup.Common.csproj", "../Tubumu.Mediasoup.Common/"]
COPY ["../Tubumu.Libuv/Tubumu.Libuv.csproj", "../Tubumu.Libuv/"]
COPY ["../Tubumu.Mediasoup.AspNetCore/Tubumu.Mediasoup.AspNetCore.csproj", "../Tubumu.Mediasoup.AspNetCore/"]
COPY ["../Tubumu.Mediasoup.Executable/Tubumu.Mediasoup.Executable.csproj", "../Tubumu.Mediasoup.Executable/"]
COPY ["../Tubumu.Meeting.Server/Tubumu.Meeting.Server.csproj", "../Tubumu.Meeting.Server/"]
COPY ["../Tubumu.Meeting.Server.Consul/Tubumu.Meeting.Server.Consul.csproj", "../Tubumu.Meeting.Server.Consul/"]
RUN dotnet restore "Tubumu.Web/Tubumu.Web.csproj"
COPY . .
WORKDIR "/src/Tubumu.Web"
RUN dotnet build "Tubumu.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Tubumu.Web.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Tubumu.Web.dll"]
