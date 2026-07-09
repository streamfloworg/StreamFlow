using StreamFlow.Core.AudioHandling;

namespace StreamFlow.Core.Persistence;

public interface IPersistenceDataManager
{
    PersistentData Load();
    bool Save(PersistentData dataToSave);

    /// <summary>
    /// Exports the given scene to a package (zip) file including all audio files and infos about the scene.
    /// </summary>
    /// <param name="exportFileName">Full path were the scene is exported to</param>
    /// <param name="sceneToExport">Scene which should be exported</param>
    /// <returns>If the operation was successful or not</returns>
    Task<bool> ExportScene(string exportFileName, Scene sceneToExport);

    /// <summary>
    /// Gets the necessary info about a scene from a package (zip) file. No audio files are imported.
    /// </summary>
    /// <param name="packageFile">Path to the package file</param>
    /// <returns>Awaitable Task which returns the Scene (or null if not found/invalid)</returns>
    Task<Scene?> PeekScene(string packageFile);

    /// <summary>
    /// Imports an scene from a package (zip) file and saves it to the scenes
    /// Also adds the necessary audio to the audio list
    /// </summary>
    /// <param name="packageFile">Full path to the package</param>
    /// <param name="saveDirectory">Directory to which the new audio files should be saved to</param>
    /// <returns>Awaitable Task</returns>
    Task ImportScene(string packageFile, string? saveDirectory);
}
