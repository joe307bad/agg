module MostRecentMovieRating

open System.Net.Http
open System.Text.Json
open System.Xml.Linq
open DotNetEnv

Env.Load() |> ignore

type MovieIds = {
    Tmdb: int64
    Trakt: int64
}

type Movie = {
    Title: string
    Year: int
    Ids: MovieIds
}

type MovieRating = {
    Movie: Movie
    Rating: int
    RatedAt: string
}

type MovieHistoryEntry = {
    Movie: Movie
    WatchedAt: string
    Rating: int option
}

let httpClient = 
    let client = new HttpClient()
    let clientId = System.Environment.GetEnvironmentVariable("TRACKT_TV_API_KEY")
    client.DefaultRequestHeaders.Add("trakt-api-key", clientId)
    client.DefaultRequestHeaders.Add("User-Agent", "F#-App/1.0")
    client

// Convert MovieHistoryEntry to RSS item XML
let movieHistoryToRssItem (movieEntry: MovieHistoryEntry) =
    let ratingText = 
        match movieEntry.Rating with
        | Some rating -> sprintf " - Rated %d/10" rating
        | None -> ""
    
    let title = sprintf "%s (%d)%s" movieEntry.Movie.Title movieEntry.Movie.Year ratingText
    let description = sprintf "Watched %s (%d) on Trakt%s" movieEntry.Movie.Title movieEntry.Movie.Year ratingText
    let pubDate = System.DateTime.Parse(movieEntry.WatchedAt).ToString("R") // RFC 1123 format
    let guid = sprintf "trakt-movie-%i-%s" movieEntry.Movie.Ids.Trakt movieEntry.WatchedAt
    let link = sprintf "https://trakt.tv/movies/%i" movieEntry.Movie.Ids.Trakt
    
    XElement(XName.Get("item"),
        XElement(XName.Get("title"), title),
        XElement(XName.Get("description"), description),
        XElement(XName.Get("link"), link),
        XElement(XName.Get("guid"), guid),
        XElement(XName.Get("pubDate"), pubDate)
    )

// Parse movie rating entry from JSON
let parseMovieRating (jsonElement: JsonElement) =
    let movie = jsonElement.GetProperty("movie")
    let ids = movie.GetProperty("ids")
    
    let movieIds = {
        Tmdb = ids.GetProperty("tmdb").GetInt64()
        Trakt = ids.GetProperty("trakt").GetInt64()
    }
    
    let movieInfo = {
        Title = movie.GetProperty("title").GetString()
        Year = movie.GetProperty("year").GetInt32()
        Ids = movieIds
    }
    
    let rating = jsonElement.GetProperty("rating").GetInt32()
    let ratedAt = jsonElement.GetProperty("rated_at").GetString()
    
    {
        Movie = movieInfo
        Rating = rating
        RatedAt = ratedAt
    }

// Get all movie ratings from Trakt
let getMovieRatings () = async {
    try
        let url = "https://api.trakt.tv/users/joe307bad/ratings/movies"
        let! response = httpClient.GetAsync(url) |> Async.AwaitTask
        let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
        
        if System.String.IsNullOrWhiteSpace(content) then
            printfn "Error: Empty ratings response from API"
            return []
        elif not (content.TrimStart().StartsWith("[") || content.TrimStart().StartsWith("{")) then
            printfn "Error: Ratings response is not JSON. Content: %s" content
            return []
        else
            let jsonDoc = JsonDocument.Parse(content)
            let entries = jsonDoc.RootElement.EnumerateArray()
            
            let ratings = 
                entries
                |> Seq.map parseMovieRating
                |> Seq.toList
            
            return ratings
            
    with
    | ex -> 
        printfn "Error getting ratings: %s" ex.Message
        return []
}

// Find rating for a specific Trakt ID
let findRatingByTraktId (ratings: MovieRating list) (traktId: int64) =
    ratings
    |> List.tryFind (fun r -> r.Movie.Ids.Trakt = traktId)
// Parse movie history entry from JSON
let parseMovieHistoryEntry (jsonElement: JsonElement) =
    let movie = jsonElement.GetProperty("movie")
    let ids = movie.GetProperty("ids")
    
    let movieIds = {
        Tmdb = ids.GetProperty("tmdb").GetInt64()
        Trakt = ids.GetProperty("trakt").GetInt64()
    }
    
    let movieInfo = {
        Title = movie.GetProperty("title").GetString()
        Year = movie.GetProperty("year").GetInt32()
        Ids = movieIds
    }
    
    let watchedAt = jsonElement.GetProperty("watched_at").GetString()
    
    {
        Movie = movieInfo
        WatchedAt = watchedAt
        Rating = None // Will be populated from ratings API
    }

// Get the most recent movie history entry and return as RSS XML
let getMostRecentMovieRatingAsRss () = async {
    try
        let url = "https://api.trakt.tv/users/joe307bad/history/movies"
        let! response = httpClient.GetAsync(url) |> Async.AwaitTask
        let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
        
        if System.String.IsNullOrWhiteSpace(content) then
            printfn "Error: Empty response from API"
            return None
        elif not (content.TrimStart().StartsWith("[") || content.TrimStart().StartsWith("{")) then
            printfn "Error: Response is not JSON. Content: %s" content
            return None
        else
            let jsonDoc = JsonDocument.Parse(content)
            let entries = jsonDoc.RootElement.EnumerateArray()
            
            let mostRecentHistoryEntry = 
                entries
                |> Seq.map parseMovieHistoryEntry
                |> Seq.tryHead
            
            match mostRecentHistoryEntry with
            | Some historyEntry -> 
                // Get all ratings to find the rating for this movie
                let! ratings = getMovieRatings()
                let movieRating = findRatingByTraktId ratings historyEntry.Movie.Ids.Trakt
                
                // Create the final entry with rating
                let entryWithRating = {
                    historyEntry with Rating = movieRating |> Option.map (fun r -> r.Rating)
                }
                
                let rssItem = movieHistoryToRssItem entryWithRating
                return Some rssItem
            | None -> 
                printfn "No entries found in response"
                return None
            
    with
    | ex -> 
        printfn "Error: %s" ex.Message
        printfn "Stack trace: %s" ex.StackTrace
        return None
}

// Get RSS XML as string
let getMostRecentMovieRatingAsRssString () = async {
    let! rssItem = getMostRecentMovieRatingAsRss()
    match rssItem with
    | Some item -> return Some (item.ToString())
    | None -> return None
}