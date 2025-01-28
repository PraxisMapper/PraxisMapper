using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PraxisCore.Support
{
    public class UserCerts
    {
        public X509Certificate2 MakeUserCertificate(string username, string userPassword)
        {
            // NOTE: For maximum security, any user-specific certificates should be generated on the user's device and not a server.
            // A server may still have good reasons to generate certificates, including talking to other servers.

            // Clients can generate their own on Godot via Crypto.generate_self_signed_certificate(). That would let clients
            // interact with intermediates that can't edit the data (EX: Alice gives Carol a gift, but has to relay that data through Bob)
            // and those may be RSA instead of ECDsa, but they'll still work for the same purpose.

            var certificateRequest = new CertificateRequest("cn=MyCertificate",
                RSA.Create(), //ECDsa.Create(),  //I wanted to use ECDH, but AP mandates RSA
                HashAlgorithmName.SHA256, 
                RSASignaturePadding.Pkcs1);

            var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(25));
            return certificate;

            //byte[] certBytes = certificate.Export(X509ContentType.Pfx, "PraxisMapper Internal");

            //var privateKey = certificate.GetECDsaPrivateKey();
            //var publicKey = certificate.GetECDsaPublicKey();

            //return this data, allow the requester to do what they want with it. Save, convert, whatever.
        }
    }
}
