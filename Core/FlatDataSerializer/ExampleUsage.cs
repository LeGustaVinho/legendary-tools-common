using System;
using System.Collections.Generic;
using FlatData;

public class Player
{
    public string Name { get; set; }

    public int Level { get; set; }

    public string[] Skills { get; set; }

    public PlayerProfile Profile { get; set; }
}

public class PlayerProfile
{
    public bool IsPremium { get; set; }

    public Address Address { get; set; }
}

public class Address
{
    public string City { get; set; }

    public int ZipCode { get; set; }
}

public static class ExampleUsage
{
    public static void Run()
    {
        Player player = new Player
        {
            Name = "Gustavo",
            Level = 10,
            Skills = new[]
            {
                "Programming",
                "Game Design",
                "3D Modeling"
            },
            Profile = new PlayerProfile
            {
                IsPremium = true,
                Address = new Address
                {
                    City = "Belo Horizonte",
                    ZipCode = 30110000
                }
            }
        };

        FlatObject flatPlayer = FlatDataSerializer.Serialize(player);

        foreach (KeyValuePair<string, object> pair in flatPlayer.Values)
        {
            Console.WriteLine($"{pair.Key} = {pair.Value ?? "null"}");
        }

        Player restoredPlayer =
            FlatDataSerializer.Deserialize<Player>(flatPlayer);

        List<Player> players = new List<Player>
        {
            player,
            new Player
            {
                Name = "Frederico",
                Level = 7,
                Skills = new[]
                {
                    "Project Management"
                },
                Profile = new PlayerProfile
                {
                    IsPremium = false,
                    Address = new Address
                    {
                        City = "Santa Luzia",
                        ZipCode = 30120000
                    }
                }
            }
        };

        FlatTable table =
            FlatDataSerializer.SerializeCollection(players);

        List<Player> restoredPlayers =
            FlatDataSerializer.DeserializeCollection<Player>(table);
    }
}
