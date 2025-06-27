# Agg - Digital Journal RSS Generator

A personal digital journal aggregator that collects activities from various platforms (GitHub, Trakt, Flickr) and generates an RSS feed. Built with F# and ASP.NET Core.

## Overview

Agg automatically aggregates my digital activities into a single RSS feed, creating a curated stream of me discoveries, thoughts, and activities. The application runs as a web server that generates and serves an RSS feed containing:

- **Most recent GitHub commit** - Latest code activity from my repositories
- **Movie and TV ratings** - Reviews from Trakt.tv
- **Photos** - Latest uploads from Flickr

The RSS feed is automatically regenerated every 12 hours and served at the root endpoint.

## Features

- **Automated aggregation** - Pulls data from multiple APIs on a schedule
- **RSS 2.0 compliant** - Standard RSS format with custom content types
- **Web hosting** - Built-in web server serves the generated RSS feed
- **Docker support** - Containerized deployment ready
- **Environment-based configuration** - Secure API key management

## Prerequisites

- .NET 9.0 SDK
- Docker (for containerized deployment)
- API keys for:
    - Flickr (for photo data)
    - Trackt (for TV/movie data)

## Configuration

Create a `.env` file in the project root with your API credentials:

```env
TRACKT_TV_API_KEY=
FLICKR_API_KEY=
```

## Running Locally

### Development Mode

```bash
# Restore dependencies
dotnet restore

# Run the application
dotnet run
```

The application will:
1. Generate an initial RSS feed
2. Start a web server on `http://localhost:5001`
3. Schedule automatic regeneration every 12 hours

### Production Build

```bash
# Build for release
dotnet publish -c Release -o ./publish

# Run the published application
cd publish
dotnet Agg.dll
```

## Docker Deployment

### Build the Docker Image

```bash
docker build -t agg .
```

### Run with Docker

```bash
docker run -d \
  --name agg-journal \
  -p 5001:5001 \
  --env-file .env \
  agg
```

## RSS Feed Structure

The generated RSS feed includes:

```xml
<rss version="2.0">
  <channel>
    <title>Joe's digital journal</title>
    <description>A curated stream of my discoveries, thoughts, and activities</description>
    <lastBuildDate>[timestamp]</lastBuildDate>
    <!-- Items with custom content types -->
    <item>
      <contentType>code-commit|movie-review|episode-review|photo</contentType>
      <!-- Standard RSS fields plus custom metadata -->
    </item>
  </channel>
</rss>
```

## Architecture

```
Agg/
├── RecentActivity/           # Data source modules
│   ├── MostRecentCommit.fs      # GitHub integration
│   ├── MostRecentMovieRating.fs # Trakt.tv integration
│   └── MostRecentPhoto.fs       # Flickr integration
├── Program.fs                # Main application and web server
├── Agg.fsproj               # Project configuration
├── Dockerfile               # Container configuration
├── .env                     # Environment variables (create this)
└── wwwroot/                 # Generated static files
    └── journal.xml          # RSS feed output
```

## Customization

To add new data sources:

1. Create a new module in `RecentActivity/`
2. Implement a function with signature: `unit -> Async<string option>`
3. Add the function to `rssItemGenerators` array in `Program.fs`
4. Return RSS `<item>` XML as a string