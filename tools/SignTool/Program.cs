// Makes and uses the Ed25519 key that signs Bismillah updates. Same key format as the Mac app's update_key.swift
// (a raw 32-byte key as base64 in a text file), so either tool can sign a release.
//
//   SignTool keygen <private-key-file>          create a new key (refuses to overwrite); prints the public key
//   SignTool pubkey <private-key-file>          print the public key (base64)
//   SignTool sign   <private-key-file> <file>   print a base64 signature of <file>
//   SignTool verify <public-key-base64> <file> <signature-file>
using Org.BouncyCastle.Math.EC.Rfc8032;
using Org.BouncyCastle.Security;

static int Fail(string message) { Console.Error.WriteLine(message); return 1; }

static byte[] LoadKey(string path) => Convert.FromBase64String(File.ReadAllText(path).Trim());

static string Public(byte[] priv)
{
    var pub = new byte[Ed25519.PublicKeySize];
    Ed25519.GeneratePublicKey(priv, 0, pub, 0);
    return Convert.ToBase64String(pub);
}

if (args.Length < 2) return Fail("usage: SignTool keygen|pubkey|sign|verify ...");
switch (args[0])
{
    case "keygen":
    {
        string path = args[1];
        if (File.Exists(path)) return Fail($"{path} already exists; not overwriting it");
        var priv = new byte[Ed25519.SecretKeySize];
        new SecureRandom().NextBytes(priv);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, Convert.ToBase64String(priv) + "\n");
        Console.WriteLine(Public(priv));
        return 0;
    }
    case "pubkey":
        Console.WriteLine(Public(LoadKey(args[1])));
        return 0;
    case "sign":
    {
        if (args.Length < 3) return Fail("usage: SignTool sign <private-key-file> <file>");
        var priv = LoadKey(args[1]);
        var data = File.ReadAllBytes(args[2]);
        var signature = new byte[Ed25519.SignatureSize];
        Ed25519.Sign(priv, 0, data, 0, data.Length, signature, 0);
        Console.WriteLine(Convert.ToBase64String(signature));
        return 0;
    }
    case "verify":
    {
        if (args.Length < 4) return Fail("usage: SignTool verify <public-key-base64> <file> <signature-file>");
        var pub = Convert.FromBase64String(args[1].Trim());
        var data = File.ReadAllBytes(args[2]);
        var sig = Convert.FromBase64String(File.ReadAllText(args[3]).Trim());
        bool ok = Ed25519.Verify(sig, 0, pub, 0, data, 0, data.Length);
        Console.WriteLine(ok ? "valid" : "INVALID");
        return ok ? 0 : 2;
    }
    default:
        return Fail($"unknown command {args[0]}");
}
