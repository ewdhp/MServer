using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using MServer.Models;

namespace MServer.Services
{
    public class StatePersistenceService
    {
        public async Task SaveNodeStatesAsync
        (string filePath, Dictionary<string,
        execState> nodeStates)
        {
            var json = JsonSerializer.Serialize(nodeStates);
            await File.WriteAllTextAsync(filePath, json);
        }

        public async Task<Dictionary
        <string, execState>>LoadNodeStatesAsync(string filePath)
        {
            if (!File.Exists(filePath))
                return new Dictionary<string, execState>();

            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize
            <Dictionary<string, execState>>(json);
        }
    }
}