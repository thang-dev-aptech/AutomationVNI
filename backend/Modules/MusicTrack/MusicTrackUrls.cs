namespace Backend.Modules.MusicTrack;

public static class MusicTrackUrls
{
    public static string Preview(Guid id) => $"/api/musictrack/{id}/preview";
}
