using UnityEngine;

// Nombres para los supervivientes controlados por la maquina.
//
// Todos llevan el prefijo NPC a proposito: de un vistazo tiene que quedar claro
// quien es una persona y quien no, sobre todo al probar cosas en solitario.
public static class NpcNames
{
    private static readonly string[] Names =
    {
        "Nieves", "Bruno", "Lola", "Ramon", "Sara", "Tono", "Iris", "Gonzalo",
        "Marta", "Quique", "Vera", "Damian", "Olga", "Nacho", "Pilar", "Cesar",
        "Rocio", "Aitor", "Elsa", "Hugo", "Nuria", "Borja", "Alba", "Teo"
    };

    // Se anade un numero corto para que dos NPCs con el mismo nombre no se
    // confundan cuando hay varios por el mapa.
    public static string Random()
    {
        string name = Names[UnityEngine.Random.Range(0, Names.Length)];
        int tag = UnityEngine.Random.Range(10, 100);

        return "NPC " + name + " " + tag;
    }
}
