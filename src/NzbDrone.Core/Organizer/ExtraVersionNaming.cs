namespace NzbDrone.Core.Organizer
{
    // How files of a movie's extra versions are named. The movie's primary version always follows the naming format.
    public enum ExtraVersionNaming
    {
        // "Movie (2020) - IMAX": Plex shows the versions as one movie, Jellyfin and Emby label them
        Label = 0,

        // "Movie (2020) {edition-IMAX}": each version is an item of its own in Plex, an edition of the movie
        PlexEdition = 1
    }
}
