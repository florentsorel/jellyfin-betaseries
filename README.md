<p align="center">
  <img src="docs/assets/betaseries_cover.jpg" alt="BetaSeries Plugin for Jellyfin" width="720" />
</p>

# Jellyfin Plugin BetaSeries

<p align="center">
  <a href="https://github.com/florentsorel/jellyfin-betaseries/actions/workflows/test.yaml">
    <img src="https://github.com/florentsorel/jellyfin-betaseries/actions/workflows/test.yaml/badge.svg" alt="Test Status" />
  </a>
  <img src="https://img.shields.io/badge/version-1.0.0.0-blue.svg" alt="Version 1.0.0.0" />
  <img src="https://img.shields.io/badge/Jellyfin-%3E%3D%2010.9.0-purple.svg" alt="Jellyfin >= 10.9.0" />
  <img src="https://img.shields.io/badge/.NET-9.0-512bd4.svg" alt=".NET 9.0" />
  <a href="LICENSE">
    <img src="https://img.shields.io/badge/license-GPL--3.0-green.svg" alt="License: GPL-3.0" />
  </a>
</p>

Ce plugin pour **[Jellyfin](https://jellyfin.org)** synchronise automatiquement vos visionnages de **films**, **séries**, **saisons** et **épisodes** avec votre compte **[BetaSeries](https://www.betaseries.com)**.

---

## 🌟 Fonctionnalités

- 🔄 **Scrobble automatique à la fin de lecture :**
  - Dès qu'un épisode ou un film atteint le seuil de complétion défini dans Jellyfin (`PlaybackStopped`), le média est automatiquement marqué comme vu sur votre compte BetaSeries avec la date et l'heure exactes de visionnage (en UTC).
  - Les lectures interrompues ou partielles sont ignorées en toute sécurité.

- ⚡ **Synchronisation des actions manuelles (Marquer vu / non vu) :**
  - Si vous marquez manuellement un film ou un épisode comme vu dans l'interface de Jellyfin, il est instantanément synchronisé vers BetaSeries.
  - Si vous **démarquez** un film ou un épisode dans Jellyfin, il est automatiquement retiré de votre historique BetaSeries (`DELETE`).
  - **Prise en charge des saisons et séries complètes :** marquer ou démarquer une saison ou une série entière propage l'action sur l'ensemble des épisodes concernés.

- 🛡️ **Protection de l'historique (`bulk=false`) :**
  - Les requêtes de marquage d'épisodes sont envoyées avec `bulk=false`. Cela garantit que BetaSeries ne marquera **jamais** les épisodes antérieurs non présents dans votre médiathèque, préservant ainsi fidèlement votre progression et vos dates d'historique.

- 👥 **Multi-utilisateurs & Multi-comptes :**
  - Créez autant de profils BetaSeries que nécessaire.
  - Associez un ou plusieurs utilisateurs Jellyfin à chaque profil BetaSeries (ou associez plusieurs utilisateurs locaux à un même compte partagé).
  - Chaque profil dispose de ses propres réglages : synchronisation des films, synchronisation des séries, scrobble en direct, synchronisation des clics manuels.

- 🔀 **Résolution intelligente des métadonnées (Fallbacks) :**
  - **Films :** recherche par TMDB ID avec repli automatique sur IMDb ID en cas de correspondance manquante ou de code 4001.
  - **Séries :** recherche par TVDB ID, repli sur IMDb ID, puis repli sur recherche textuelle validée par TMDB ID.
  - **Épisodes :** recherche directe par TVDB ID de l'épisode ou résolution par saison et numéro d'épisode (SxxExx).

- ⏱️ **Gestion de l'idempotence et anti-rebond (Debounce) :**
  - Un cache anti-rebond de 30 secondes en mémoire empêche l'envoi de requêtes répétées ou de doublons lors d'événements simultanés.
  - Les réponses de BetaSeries signalant qu'un média est déjà vu ou déjà démarqué sont traitées gracieusement sans générer d'erreurs.

- 🔐 **Authentification OAuth2 simplifiée :**
  - Associez votre compte directement depuis la page de configuration du plugin via une fenêtre popup d'autorisation OAuth2.

---

## ⚙️ Prérequis

1. Un serveur **Jellyfin 10.9.0 ou supérieur** (tournant sous .NET 9).
2. Un compte **[BetaSeries](https://www.betaseries.com)**.
3. Une clé d'API (Client ID) BetaSeries :
   - Rendez-vous sur **[https://www.betaseries.com/en/account/api](https://www.betaseries.com/en/account/api)**.
   - Créez une application pour obtenir votre **Clé d'application** (Client ID) et votre **Secret**.

---

## 🚀 Configuration dans Jellyfin

1. Dans Jellyfin, accédez au **Tableau de bord** > **Extensions** > **BetaSeries**.
2. **Paramètres de l'application :**
   - Renseignez votre **Client ID** (clé d'application).
   - Renseignez votre **Client Secret** (secret OAuth de l'application).
   - Cliquez sur **Enregistrer les clés d'application**.
3. **Ajout d'un profil BetaSeries :**
   - Cliquez sur **+ Ajouter un profil**.
   - Donnez un nom au profil (ex : *Mon compte BetaSeries*).
   - Sélectionnez le ou les utilisateurs Jellyfin associés.
   - Configurez les options souhaitées (Synchroniser les séries, Synchroniser les films, Scrobbler les lectures terminées, Synchroniser les modifications manuelles).
   - Cliquez sur **🔗 Se connecter avec BetaSeries (OAuth2)** pour autoriser et associer votre compte via la fenêtre popup.
   - Cliquez sur **Enregistrer le profil**.

---

## 📦 Installation

### Méthode 1 : Via le catalogue de dépôts Jellyfin (Recommandé)

1. Dans votre interface Jellyfin, allez dans **Tableau de bord** > **Plugins** > onglet **Dépôts**.
2. Cliquez sur le bouton **+** pour ajouter un nouveau dépôt :
   - **Nom du dépôt :** `BetaSeries`
   - **URL du dépôt :** `https://raw.githubusercontent.com/florentsorel/jellyfin-betaseries/master/manifest.json`
3. Allez dans l'onglet **Catalogue**, sélectionnez **BetaSeries** et cliquez sur **Installer**.
4. Redémarrez votre serveur Jellyfin.

### Méthode 2 : Installation manuelle

1. Rendez-vous sur la page des [Releases](https://github.com/florentsorel/jellyfin-betaseries/releases) et téléchargez la dernière version.
2. Créez un dossier `BetaSeries` dans le répertoire des plugins de votre serveur Jellyfin :
   - **Linux :** `/var/lib/jellyfin/plugins/BetaSeries/`
   - **Windows :** `%ProgramData%\Jellyfin\Server\plugins\BetaSeries\`
   - **Docker :** `/config/plugins/BetaSeries/`
3. Déposez-y les fichiers `Jellyfin.Plugin.BetaSeries.dll` et `betaseries_cover.jpg`.
4. Redémarrez le serveur Jellyfin :
   ```bash
   sudo systemctl restart jellyfin
   ```

---

## 🛠️ Développement et Tests

### Compilation

```bash
# Compilation en mode Release
dotnet build -c Release
```

### Lancement de la suite de tests

Le projet inclut une suite complète de tests unitaires (xUnit, Moq) isolant tous les flux HTTP via un mock hermétique (`MockHttpMessageHandler`) :

```bash
# Exécution de tous les tests unitaires
dotnet test
```

Périmètre couvert par les tests :
- Validation des réglages et profils (`PluginConfiguration`, sérialisation XML).
- Client API (`BetaSeriesClient`) : résolution de films, séries, épisodes, scrobble avec date UTC, `bulk=false`, démarquage, gestion des codes 4001 et erreurs HTTP.
- Gestionnaire d'événements (`BetaSeriesManager`) : écoute `PlaybackStopped`, gestion `UserDataSaved`, filtrage des doublons `PlaybackFinished`, anti-rebond mémoire, traitement des saisons et séries complètes.

---

## 📄 Licence

Ce projet est distribué sous licence [GPL-3.0](LICENSE).
