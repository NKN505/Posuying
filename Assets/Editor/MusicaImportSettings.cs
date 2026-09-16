using UnityEditor;
using UnityEngine;

/// <summary>
/// Ajustes de importacion para la musica: cualquier audio que se meta en una
/// carpeta "Musica" se importa en streaming y comprimido.
///
/// Por que: con los ajustes por defecto Unity descomprime la cancion entera en
/// memoria al cargarla. El tema del menu dura 2 minutos: en streaming ocupa unos
/// pocos cientos de KB en RAM en vez de ~22 MB.
///
/// Solo actua si el fichero sigue con el tipo de carga por defecto (descomprimir
/// al cargar). Si eliges a mano otro tipo en el inspector, se respeta.
///
/// No se usa importSettingsMissing a proposito: si la cancion llega a importarse
/// antes de que este script compile, el .meta ya existe con los valores por
/// defecto y ese chequeo haria que no se corrigiera nunca, ni reimportando.
/// </summary>
public class MusicaImportSettings : AssetPostprocessor
{
    void OnPreprocessAudio()
    {
        if (!assetPath.Replace('\\', '/').Contains("/Musica/")) return;

        var importer = (AudioImporter)assetImporter;

        var ajustes = importer.defaultSampleSettings;
        if (ajustes.loadType != AudioClipLoadType.DecompressOnLoad) return;

        ajustes.loadType = AudioClipLoadType.Streaming;
        ajustes.compressionFormat = AudioCompressionFormat.Vorbis;
        ajustes.quality = 0.7f;
        importer.defaultSampleSettings = ajustes;

        // Es musica estereo: no se fuerza a mono
        importer.forceToMono = false;
        importer.loadInBackground = true;
    }
}
