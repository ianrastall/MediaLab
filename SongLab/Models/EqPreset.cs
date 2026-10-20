namespace SongLab.Models;

public sealed record EqPreset(
    string Name,
    double HighPassHz,
    double LowPassHz,
    double BassDb,
    double TrebleDb);
