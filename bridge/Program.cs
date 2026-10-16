using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Security.Cryptography;

/// Plant Bridge: connects to the Plant API local TCP server
/// (127.0.0.1:25666) and re-broadcasts every event line to the phone
/// over Bluetooth LE (GATT notifications, 20-byte chunked frames).
///
/// Run this on the PC. Keep the window open.
class Program
{
    static readonly Guid ServiceUuid = Guid.Parse("9F2A7C44-8B31-4E6D-A5C2-7D0E9F1B3A66");
    static readonly Guid CharUuid = Guid.Parse("9F2A7C44-8B31-4E6D-A5C2-7D0E9F1B3A67");

    static GattLocalCharacteristic EventChar;
    static volatile bool HaveSubscriber = false;
    static long SentMessages = 0;
    static long SentChunks = 0;
    static string LastStateLine;

    static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("Plant Bridge - local events -> Bluetooth LE");
        Console.WriteLine("-------------------------------------------");

        var result = await GattServiceProvider.CreateAsync(ServiceUuid);
        if (result.Error != BluetoothError.Success)
        {
            Console.WriteLine($"BLE error: could not create service ({result.Error}). Is Bluetooth on?");
            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
            return 1;
        }
        var provider = result.ServiceProvider;

        var charParams = new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Notify,
            UserDescription = "Plant API events",
        };
        var charResult = await provider.Service.CreateCharacteristicAsync(CharUuid, charParams);
        if (charResult.Error != BluetoothError.Success)
        {
            Console.WriteLine($"BLE error: could not create characteristic ({charResult.Error})");
            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
            return 1;
        }
        EventChar = charResult.Characteristic;

        EventChar.SubscribedClientsChanged += (s, e) =>
        {
            HaveSubscriber = EventChar.SubscribedClients.Count > 0;
            Console.WriteLine(HaveSubscriber ? ">> Phone connected" : ">> Phone disconnected");
            if (HaveSubscriber && LastStateLine != null)
            {
                _ = SendLineToPhone(LastStateLine);
            }
        };
        provider.AdvertisementStatusChanged += (s, e) =>
            Console.WriteLine($">> BLE advertising: {provider.AdvertisementStatus}");

        provider.StartAdvertising(new GattServiceProviderAdvertisingParameters
        {
            IsConnectable = true,
            IsDiscoverable = true,
        });
        Console.WriteLine("BLE ready - waiting for the phone (select Bluetooth in the app and connect).");

        _ = Task.Run(TcpLoop);

        Console.WriteLine();
        Console.WriteLine("Waiting for the local service on 127.0.0.1:25666 ...");
        Console.WriteLine("Keep this window open. Ctrl+C to exit.");
        await Task.Delay(Timeout.Infinite);
        return 0;
    }

    static async Task TcpLoop()
    {
        while (true)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", 25666);
                Console.WriteLine(">> Source connected.");
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                while (true)
                {
                    var line = await reader.ReadLineAsync();
                    if (line == null) break;
                    if (line.Length == 0 || line.Length > 2300) continue;
                    if (line.Contains("\"type\":\"constants\"")) LastStateLine = line;
                    await SendLineToPhone(line);
                }
                Console.WriteLine(">> Source closed the connection.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($">> Waiting for the local service: {ex.Message}");
            }
            await Task.Delay(2000);
        }
    }

    static async Task SendLineToPhone(string line)
    {
        if (!HaveSubscriber) return;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(line);
            int chunks = (bytes.Length + 18) / 19; // 19 data bytes per chunk + 1 header byte
            for (int i = 0; i < chunks; i++)
            {
                int offset = i * 19;
                int len = Math.Min(19, bytes.Length - offset);
                var payload = new byte[1 + len];
                payload[0] = (byte)((i << 1) | (i == chunks - 1 ? 1 : 0));
                Array.Copy(bytes, offset, payload, 1, len);
                var buffer = CryptographicBuffer.CreateFromByteArray(payload);
                await EventChar.NotifyValueAsync(buffer);
            }
            SentMessages++;
            SentChunks += chunks;
            if (SentMessages % 25 == 1)
            {
                Console.WriteLine($">> Forwarded {SentMessages} events ({SentChunks} BLE chunks). Last: {Trim(line, 60)}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($">> BLE send error: {ex.Message}");
        }
    }

    static string Trim(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "...";
}
