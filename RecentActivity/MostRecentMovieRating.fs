module MostRecentMovieRating

open System.Net.Http
open System.Text.Json
open System.Xml.Linq
open DotNetEnv

Env.Load() |> ignore

type Ids = {
    Tmdb: int64
    Trakt: int64
}

type Episode = {
    Title: string
    ShowTitle: string
    Season: int
    Number: int
    Ids: Ids
}

type Movie = {
    Title: string
    Year: int
    Ids: Ids
}

type MovieRating = {
    Movie: Movie
    Rating: int
    RatedAt: string
}

type EpisodeRating = {
    Episode: Episode
    Rating: int
    RatedAt: string
}

type MovieHistoryEntry = {
    Movie: Movie
    WatchedAt: string
    Rating: int option
}

type EpisodeHistoryEntry = {
    Episode: Episode
    WatchedAt: string
    Rating: int option
}

let httpClient = 
    let client = new HttpClient()
    let clientId = System.Environment.GetEnvironmentVariable("TRACKT_TV_API_KEY")
    client.DefaultRequestHeaders.Add("trakt-api-key", clientId)
    client.DefaultRequestHeaders.Add("User-Agent", "F#-App/1.0")
    client

let movieHistoryToRssItem (movieEntry: MovieHistoryEntry) =
    let ratingText = 
        match movieEntry.Rating with
        | Some rating -> sprintf " - %d/10" rating
        | None -> ""
    
    let title = sprintf "%s (%d)%s" movieEntry.Movie.Title movieEntry.Movie.Year ratingText
    let description = sprintf "%s (%d)%s" movieEntry.Movie.Title movieEntry.Movie.Year ratingText
    let pubDate = System.DateTime.SpecifyKind(System.DateTime.Parse(movieEntry.WatchedAt), System.DateTimeKind.Utc)
    let guid = sprintf "trakt-movie-%i-%s" movieEntry.Movie.Ids.Trakt movieEntry.WatchedAt
    let link = sprintf "https://trakt.tv/movies/%i" movieEntry.Movie.Ids.Trakt
    
    XElement(XName.Get("item"),
        XElement(XName.Get("title"), title),
        XElement(XName.Get("description"), description),
        XElement(XName.Get("link"), link),
        XElement(XName.Get("guid"), guid),
        XElement(XName.Get("pubDate"), pubDate),
        
        XElement(XName.Get("contentType"),"movie-review"),
        XElement(XName.Get("movieTitle"), movieEntry.Movie.Title),
        XElement(XName.Get("releaseYear"), movieEntry.Movie.Year),
        XElement(XName.Get("rating"), movieEntry.Rating |> Option.defaultValue -1)
    )

let episodeHistoryToRssItem (episodeEntry: EpisodeHistoryEntry) =
    let ratingText = 
        match episodeEntry.Rating with
        | Some rating -> sprintf " - %d/10" rating
        | None -> ""
    
    let title = sprintf "%s%s" episodeEntry.Episode.Title ratingText
    let description = sprintf "%s - %s (Season %d / Episode %d)%s" episodeEntry.Episode.ShowTitle episodeEntry.Episode.Title episodeEntry.Episode.Season episodeEntry.Episode.Number ratingText
    let pubDate = System.DateTime.SpecifyKind(System.DateTime.Parse(episodeEntry.WatchedAt), System.DateTimeKind.Utc)
    let guid = sprintf "trakt-episode-%i-%s" episodeEntry.Episode.Ids.Trakt episodeEntry.WatchedAt
    let link = sprintf "https://trakt.tv/episodes/%i" episodeEntry.Episode.Ids.Trakt
    
    XElement(XName.Get("item"),
        XElement(XName.Get("title"), title),
        XElement(XName.Get("description"), description),
        XElement(XName.Get("link"), link),
        XElement(XName.Get("guid"), guid),
        XElement(XName.Get("pubDate"), pubDate),
        
        XElement(XName.Get("contentType"),"episode-review"),
        XElement(XName.Get("episodeTitle"), episodeEntry.Episode.Title),
        XElement(XName.Get("season"), episodeEntry.Episode.Season),
        XElement(XName.Get("number"), episodeEntry.Episode.Number),
        XElement(XName.Get("showTitle"), episodeEntry.Episode.ShowTitle),
        XElement(XName.Get("rating"), episodeEntry.Rating |> Option.defaultValue -1)
    )

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

let parseEpisodeRating (jsonElement: JsonElement) =
    let episode = jsonElement.GetProperty("episode")
    let ids = episode.GetProperty("ids")
    let show = jsonElement.GetProperty("show")
    
    let ids = {
        Tmdb = ids.GetProperty("tmdb").GetInt64()
        Trakt = ids.GetProperty("trakt").GetInt64()
    }
    
    let episodeInfo = {
        Title = episode.GetProperty("title").GetString()
        ShowTitle = show.GetProperty("title").GetString()
        Season = episode.GetProperty("season").GetInt32()
        Number = episode.GetProperty("number").GetInt32()
        Ids = ids
    }
    
    let rating = jsonElement.GetProperty("rating").GetInt32()
    let ratedAt = jsonElement.GetProperty("rated_at").GetString()
    
    {
        Episode = episodeInfo
        Rating = rating
        RatedAt = ratedAt
    }

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

let getEpisodeRatings () = async {
    try
        let url = "https://api.trakt.tv/users/joe307bad/ratings/episodes"
        let! response = httpClient.GetAsync(url) |> Async.AwaitTask
        let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
        
        if System.String.IsNullOrWhiteSpace(content) then
            printfn "Error: Empty episode ratings response from API"
            return []
        elif not (content.TrimStart().StartsWith("[") || content.TrimStart().StartsWith("{")) then
            printfn "Error: Episode Ratings response is not JSON. Content: %s" content
            return []
        else
            let jsonDoc = JsonDocument.Parse(content)
            let entries = jsonDoc.RootElement.EnumerateArray()
            
            let ratings = 
                entries
                |> Seq.map parseEpisodeRating
                |> Seq.toList
            
            return ratings
            
    with
    | ex -> 
        printfn $"Error getting ratings: %s{ex.Message}"
        return []
}

let findRatingByTraktId (ratings: MovieRating list) (traktId: int64) =
    ratings
    |> List.tryFind (fun r -> r.Movie.Ids.Trakt = traktId)

let findEpisodeRatingByTraktId (ratings: EpisodeRating list) (traktId: int64) =
    ratings
    |> List.tryFind (fun r -> r.Episode.Ids.Trakt = traktId)

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
        Rating = None
    }



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
                let! ratings = getMovieRatings()
                let movieRating = findRatingByTraktId ratings historyEntry.Movie.Ids.Trakt
                
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


let parseEpisodeHistoryEntry (jsonElement: JsonElement) =
    let episode = jsonElement.GetProperty("episode")
    let episodeIds = episode.GetProperty("ids")
    let show = jsonElement.GetProperty("show")
    
    let episodeIdsRecord = {
        Tmdb = episodeIds.GetProperty("tmdb").GetInt64()
        Trakt = episodeIds.GetProperty("trakt").GetInt64()
    }
    
    let episodeInfo = {
        Title = episode.GetProperty("title").GetString()
        Season = episode.GetProperty("season").GetInt32()
        Number = episode.GetProperty("number").GetInt32()
        ShowTitle = show.GetProperty("title").GetString()
        Ids = episodeIdsRecord
    }
    
    let watchedAt = jsonElement.GetProperty("watched_at").GetString()
    
    {
        Episode = episodeInfo
        WatchedAt = watchedAt
        Rating = None
    }

let getMostRecentEpisodeRatingAsRss () = async {
    try
        let url = "https://api.trakt.tv/users/joe307bad/history/episodes"
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
            
            let mostRecentEpisodeEntry = 
                entries
                |> Seq.map parseEpisodeHistoryEntry
                |> Seq.tryHead
            
            match mostRecentEpisodeEntry with
            | Some historyEntry -> 
                let! ratings = getEpisodeRatings()
                let episodeRating = findEpisodeRatingByTraktId ratings historyEntry.Episode.Ids.Trakt
                
                let entryWithRating = {
                    historyEntry with Rating = episodeRating |> Option.map (fun r -> r.Rating)
                }
                
                let rssItem = episodeHistoryToRssItem entryWithRating
                return Some rssItem
            | None -> 
                printfn "No episode entries found in response"
                return None   
    with
    | ex -> 
        printfn "Error: %s" ex.Message
        printfn "Stack trace: %s" ex.StackTrace
        return None
}

let getMostRecentMovieRatingAsRssString () = async {
    let! rssItem = getMostRecentMovieRatingAsRss()
    match rssItem with
    | Some item -> return Some (item.ToString())
    | None -> return None
}

let getMostRecentEpisodeRatingAsRssString () = async {
    let! rssItem = getMostRecentEpisodeRatingAsRss()
    match rssItem with
    | Some item -> return Some (item.ToString())
    | None -> return None
}