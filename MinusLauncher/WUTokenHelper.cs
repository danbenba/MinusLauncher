// Utilisation des bibliothèques nécessaires
using System;
using System.Runtime.InteropServices;
using Windows.Security.Authentication.Web.Core;

namespace MCLauncher
{
    // Classe d'assistance pour obtenir le jeton Windows Update
    class WUTokenHelper
    {

        // Méthode publique pour obtenir le jeton WU
        public static string GetWUToken()
        {
            try
            {
                string token;
                int status = GetWUToken(out token);
                // Gestion des erreurs spécifiques à Windows Update
                if (status >= WU_ERRORS_START && status <= WU_ERRORS_END)
                    throw new WUTokenException(status);
                else if (status != 0)
                    Marshal.ThrowExceptionForHR(status);
                return token;
            }
            catch (SEHException e)
            {
                Marshal.ThrowExceptionForHR(e.HResult);
                return ""; // Retour vide en cas d'erreur
            }
        }

        // Définition des constantes pour la gestion des erreurs
        private const int WU_ERRORS_START = unchecked((int)0x80040200);
        private const int WU_NO_ACCOUNT = unchecked((int)0x80040200);

        private const int WU_TOKEN_FETCH_ERROR_BASE = unchecked((int)0x80040300);
        private const int WU_TOKEN_FETCH_ERROR_END = unchecked((int)0x80040400);

        private const int WU_ERRORS_END = unchecked((int)0x80040400);

        // Importation de la méthode externe GetWUToken de la DLL WUTokenHelper
        [DllImport("WUTokenHelper.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int GetWUToken([MarshalAs(UnmanagedType.LPWStr)] out string token);

        // Classe interne pour les exceptions spécifiques à WUToken
        public class WUTokenException : Exception
        {
            public WUTokenException(int exception) : base(GetExceptionText(exception))
            {
                HResult = exception;
            }
            // Méthode pour obtenir le texte de l'exception basée sur le code d'erreur
            private static String GetExceptionText(int e)
            {
                if (e >= WU_TOKEN_FETCH_ERROR_BASE && e < WU_TOKEN_FETCH_ERROR_END)
                {
                    var actualCode = (byte)e & 0xff;
                    // Vérification de la validité du code d'erreur
                    if (!Enum.IsDefined(typeof(WebTokenRequestStatus), e))
                    {
                        return $"WUTokenHelper a retourné un HRESULT invalide : {e} (C'EST UN BUG)";
                    }
                    var status = (WebTokenRequestStatus)Enum.ToObject(typeof(WebTokenRequestStatus), actualCode);
                    switch (status)
                    {
                        // Gestion des différents statuts de la requête de jeton
                        case WebTokenRequestStatus.Success:
                            return "Succès (C'EST UN BUG)";
                        case WebTokenRequestStatus.UserCancel:
                            return "L'utilisateur a annulé la requête de jeton (C'EST UN BUG)";
                        case WebTokenRequestStatus.AccountSwitch:
                            return "L'utilisateur a demandé un changement de compte (C'EST UN BUG)";
                        case WebTokenRequestStatus.UserInteractionRequired:
                            return "Interaction utilisateur requise pour compléter la requête de jeton (C'EST UN BUG)";
                        case WebTokenRequestStatus.AccountProviderNotAvailable:
                            return "Les services de compte Xbox Live ne sont pas disponibles actuellement";
                        case WebTokenRequestStatus.ProviderError:
                            return "Erreur Xbox Live inconnue";
                    }
                }
                // Gestion des autres codes d'erreur
                switch (e)
                {
                    case WU_NO_ACCOUNT: return "Aucun compte Microsoft trouvé";
                    default: return "Erreur inconnue " + e;
                }
            }
        }

    }
}
