using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
namespace TerminalLink;
internal sealed class Identity : IDisposable {
 internal sealed class Settings { public bool AutoShareTerminals{get;set;}  public string? PeerFingerprint{get;set;} public string? PeerName{get;set;} public int Port{get;set;}=49443; public string Endpoint{get;set;}=""; }
 readonly string directory;
 internal string DirectoryPath=>directory;
 internal Settings Config;
 internal X509Certificate2 Certificate;
 internal string Fingerprint=>Hash(Certificate);
 internal string? PairToken;
 internal DateTime PairUntil;
 readonly object sync=new();
 internal Identity(string? folder=null) {
  directory=folder??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TerminalLink");
  Directory.CreateDirectory(directory);
  string certPath=Path.Combine(directory,"identity.dpapi");
  if(File.Exists(certPath)){
   byte[] pfx=ProtectedData.Unprotect(File.ReadAllBytes(certPath),null,DataProtectionScope.CurrentUser);
   try{Certificate=X509CertificateLoader.LoadPkcs12(pfx,null,X509KeyStorageFlags.UserKeySet);}finally{CryptographicOperations.ZeroMemory(pfx);}
  }else{
   using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
   var req=new CertificateRequest("CN=TerminalLink PC",key,HashAlgorithmName.SHA256);
   req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature,true));
   req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection{new("1.3.6.1.5.5.7.3.1")},false));
   using var cert=req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddYears(10));
   byte[] pfx=cert.Export(X509ContentType.Pfx);
   try {File.WriteAllBytes(certPath,ProtectedData.Protect(pfx,null,DataProtectionScope.CurrentUser));Certificate=X509CertificateLoader.LoadPkcs12(pfx,null,X509KeyStorageFlags.UserKeySet);}
   finally{CryptographicOperations.ZeroMemory(pfx);}
  }
  var path=Path.Combine(directory,"settings.json");
  Config=File.Exists(path)?JsonSerializer.Deserialize<Settings>(File.ReadAllText(path))??new():new();
 }
 internal static string Hash(X509Certificate cert)=>Convert.ToHexString(SHA256.HashData(cert.GetRawCertData()));
 internal void Save(){File.WriteAllText(Path.Combine(directory,"settings.json"),JsonSerializer.Serialize(Config,new JsonSerializerOptions{WriteIndented=true}));}
 internal bool PairingOpen {get{lock(sync)return Config.PeerFingerprint==null&&PairToken!=null&&DateTime.UtcNow<PairUntil;}}
 internal string OpenPairing(string endpoint,int port) {
  lock(sync){
   if(Config.PeerFingerprint!=null)throw new InvalidOperationException("등록된 휴대폰을 먼저 해제하세요.");
   Config.Endpoint=endpoint;Config.Port=port;Save();
   PairToken=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));PairUntil=DateTime.UtcNow.AddMinutes(5);
   var json=JsonSerializer.SerializeToUtf8Bytes(new{v=1,host=endpoint,port,pin=Fingerprint,token=PairToken});
   return "tr1:"+Convert.ToBase64String(json).TrimEnd('=').Replace('+','-').Replace('/','_');
  }
 }
 internal bool AcceptsCertificate(X509Certificate? cert) {
  if(cert==null)return false;
  return Hash(cert)==Config.PeerFingerprint||PairingOpen;
 }
 internal bool IsTrusted(string fingerprint)=>Config.PeerFingerprint==fingerprint;
 internal bool CheckPairToken(string token) {
  lock(sync){return PairingOpen&&token.Length==64&&CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(token),System.Text.Encoding.ASCII.GetBytes(PairToken!));}
 }
 internal bool Register(string fingerprint,string name,string token) {
  lock(sync){if(!CheckPairToken(token))return false;Config.PeerFingerprint=fingerprint;Config.PeerName=name;PairToken=null;Save();return true;}
 }
 internal void ClosePairing(){lock(sync)PairToken=null;}
 internal void Revoke(){lock(sync){Config.PeerFingerprint=null;Config.PeerName=null;PairToken=null;Save();}}
 public void Dispose()=>Certificate.Dispose();
}