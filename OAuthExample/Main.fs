namespace OAuthExample

open WebSharper
open WebSharper.Sitelets
open WebSharper.UI
open WebSharper.UI.Server
open WebSharper.UI.Html

module Templating =

    type MainTemplate = Templating.Template<"Main.html", serverLoad = Templating.ServerLoad.WhenChanged>

    // Compute a menubar where the menu item for the given endpoint is active
    let MenuBar (ctx: Context<EndPoint>) endpoint : Doc list =
        let ( => ) txt act =
             li [if endpoint = act then yield attr.``class`` "active"] [
                a [attr.href (ctx.Link act)] [text txt]
             ]
        [
            "Home" => EndPoint.Home
            "Private section" => EndPoint.Private
            "My repositories" => EndPoint.Repos // <- new
        ]

    let Main ctx action (title: string) (body: Doc list) =
        Content.Page(
            MainTemplate()
                .Title(title)
                .MenuBar(MenuBar ctx action)
                .Body(body)
                .Doc()
        )

module Site =

    let NotLoggedInErrorMessage (ctx: Context<EndPoint>) =
        Templating.MainTemplate.PrivateLoggedInContent()
            .GitHubLoginUrl(Auth.GitHub.Provider.GetAuthorizationRequestUrl(ctx))
            .FacebookLoginUrl(Auth.Facebook.Provider.GetAuthorizationRequestUrl(ctx))
            .Doc()

    let HomePage ctx =
        Templating.Main ctx EndPoint.Home "Home" [
            Templating.MainTemplate.HomeContent()
                .InstructionsAttr(if Auth.IsConfigured then attr.``class`` "hidden" else Attr.Empty)
                .Doc()
        ]

    let PrivatePage (ctx: Context<EndPoint>) = async {
        let! loggedIn = ctx.UserSession.GetLoggedInUser()
        let body =
            match loggedIn |> Option.bind Database.TryGetUser with
            | Some user ->
                Templating.MainTemplate.PrivateNotLoggedInContent()
                    .Username(user.DisplayName)
                    .Doc()
            | None ->
                NotLoggedInErrorMessage ctx
        return! Templating.Main ctx EndPoint.Private "Private section" [body]
    }

    let ReposPage (ctx: Context<EndPoint>) = async {
        let! loggedIn = ctx.UserSession.GetLoggedInUser()
        let! body = async {
            match loggedIn |> Option.bind Database.TryGetUser with
            | Some user ->
                // If we are logged in...
                match user.OAuthUserId with
                | OAuthProvider.GitHub, githubUserId ->
                    // ... with GitHub, then get the repositories and display them.
                    let! repositories = Repositories.GetUserRepositories user.OAuthToken
                    return Doc.Concat [
                        h1 [] [text (user.DisplayName + "'s GitHub repositories")]
                        ul [] [
                            repositories
                            |> Array.map (fun repo ->
                                li [] [
                                    a [attr.href (sprintf "https://github.com/%s/%s" githubUserId repo.html_url)] [text repo.name]
                                ] :> Doc
                            )
                            |> Doc.Concat
                        ]
                    ]
                | _ ->
                    // ... with Facebook, then show an error message.
                    return p [] [text "You must be logged in with GitHub to see this content."] :> Doc
            | None ->
                // If we are not logged in, then show an error message.
                return NotLoggedInErrorMessage ctx
        }
        return! Templating.Main ctx EndPoint.Private "My GitHub repositories" [body]
    }

    let LogoutPage (ctx: Context<EndPoint>) = async {
        do! ctx.UserSession.Logout()
        return! Content.RedirectTemporary EndPoint.Home
    }

    [<Website>]
    let Main =
        Auth.Sitelet
        <|>
        Application.MultiPage (fun ctx endpoint ->
            match endpoint with
            | EndPoint.Home -> HomePage ctx
            | EndPoint.Private -> PrivatePage ctx
            | EndPoint.Logout -> LogoutPage ctx
            | EndPoint.Repos -> ReposPage ctx
            // This is already handled by Auth.Sitelet above:
            | EndPoint.OAuth _ -> Content.ServerError
        )
