using UnityEngine;
using System;
using System.Collections;

using Sfs2X;
using Sfs2X.Core;
using Sfs2X.Entities;
using Sfs2X.Entities.Data;
using Sfs2X.Requests;
using Sfs2X.Logging;

/* --------------------------------------------------------
 * VerNetworkManager wickelt die ganze C/S Kommunikation ab
 * In jeder Szene, in der gespielt werden soll, muss
 * VerNetworkManager an einem leeren GameObject (z.B. "Game")
 * hängen.
 * 
 * Unterstützte Messages an den Server:
 * - spawnMe		(Mich als neuen Spieler spawnen lassen)
 * - sendTransform	(Transform Daten an Server, wenn ich mich bewege)
 * - chatMsg		(chatMsg an alle schicken)
 * - privMsg		(privMsg an einen speziellen Spieler)
 * 
 * Unterstützte Messages vom Server:
 * - spawnChar	(ich muss entweder mich oder jemand anderes spawnen)
 * - receiveChatMsg	(chatMsg erhalten, die ausgegeben werden muss)
 * 
 * --------------------------------------------------------
 */
public class VerNetworkManager : MonoBehaviour {

	// ----------------------------------------------------
	// Declare Variables
	// ----------------------------------------------------

	// Define Self
	private static VerNetworkManager thisInstance;
	public static VerNetworkManager ThisInstance {
		get {
			return thisInstance;
		}
	}
	
	private SmartFox sfs;				// Reference to the SFS client
	private bool thisRunning = false;	// Did the startup go well?
	private string settingNameFallbackScene;	// Wohin bei Problemen?
	private string settingDefaultOtherCharPrefab;
	
	
	/* ------------------------------------------------------
	 * Basic initialization of the game
	 * ------------------------------------------------------
	 */
	void Awake () {
		thisInstance = this;
	}
	
	/* ------------------------------------------------------
	 * Startup of the game
	 * ------------------------------------------------------
	 */	
	void Start () {
		
		// Startwerte aus den Settings beziehen
		settingNameFallbackScene = VerSettings.NameFallbackScene;
		settingDefaultOtherCharPrefab = VerSettings.DefaultOtherCharPrefab;
		
		if (!VerSmartfoxConnection.IsInitialized) {
			Debug.Log ("[" + this.name + "] [" + this.GetType ().Name + "] Connection Instance not found. Back to Fallback Scene " + settingNameFallbackScene);
			Application.LoadLevel (settingNameFallbackScene);
		} 
		else {
			sfs = VerSmartfoxConnection.Connection;
			
			RegisterSFSSceneCallbacks();	// Register all Event Listeners for communication with SFS
			SendSpawnRequest();				// Spawn me!
			
			thisRunning = true;
		}
	}
	
	/* ------------------------------------------------------
	 * Update is called once per frame
	 * 
	 * FixedUpdate as well, but it handles server events
	 * in queued mode
	 * ------------------------------------------------------
	 */
	void FixedUpdate () {

		if(!thisRunning)
			return;
		
		sfs.ProcessEvents();
		
	}
	
	/* ====================================================
	 * Callbacks für SFS einrichten
	 * 
	 * Wir können davon ausgehen, dass der Login schon
	 * erfolgt ist, sonst wären wir gar nicht in einer
	 * "spielbaren" Szene, sondern noch beim Login GUI
	 * Daher braucht es hier keine LOGIN Callbacks
	 * Gleiches gilt für den Room, der bereits im Login
	 * GUI betreten wird (sofern davor alles geklappt hat)
	 * ====================================================
	 */
	void RegisterSFSSceneCallbacks() {

		sfs.AddEventListener(SFSEvent.EXTENSION_RESPONSE, OnExtensionResponse);
		sfs.AddEventListener(SFSEvent.USER_EXIT_ROOM, OnUserExitRoom);
		sfs.AddEventListener(SFSEvent.CONNECTION_LOST, OnConnectionLost);
		sfs.AddEventListener(SFSEvent.PUBLIC_MESSAGE, OnPublicMessage);
		sfs.AddEventListener(SFSEvent.ADMIN_MESSAGE, OnPublicMessage);

		Debug.Log("[" + this.name + "] [" + this.GetType().Name + "] Callbacks für die Szene " + Application.loadedLevelName + " sind eingerichtet");

	}
	
	// ----------------------------------------------------
	private void OnExtensionResponse(BaseEvent _evt) {
		
		Debug.Log("[" + this.name + "] [" + this.GetType().Name + "] Bin in OnExtensionResponse");

		try {
			
			// Das erhaltene Datenpaket auseinandernehmen
			// CMD und PARAMS sind die zwei wichtigsten
			// Komponenten
			string _cmd = (string)_evt.Params["cmd"];
			ISFSObject _params = (SFSObject)_evt.Params["params"];
			
			Debug.Log("[" + this.name + "] [" + this.GetType().Name + "] Folgenden Response vom Server erhalten: " + _cmd);
			Debug.Log("[" + this.name + "] [" + this.GetType().Name + "] Inhalt von [msg]: " + (string)_params.GetUtfString("msg"));
			
			// Auf die verschiedenen Server Messages reagieren
			if (_cmd == "spawnChar") {
				HandleSpawnChar(_params);
			}
			else if (_cmd == "receiveChatMsg") {
				HandleReceiveChatMessage(_params);
			}
		}
		catch (Exception _e) {
			Debug.Log("[" + this.name + "] [" + this.GetType().Name + "] Exception handling response: " + _e.Message+" >>> " + _e.StackTrace);
		}
	}
	
	
	// ----------------------------------------------------
	private void OnConnectionLost (BaseEvent _evt) {
		Debug.Log("[" + this.name + "] [" + this.GetType().Name + "] Connection verloren. Fallback auf Scene " + settingNameFallbackScene);
		Application.LoadLevel (settingNameFallbackScene);
	}
	
	
	// ----------------------------------------------------
	private void OnUserExitRoom(BaseEvent _evt) {
		User _user = (User)_evt.Params["user"];
		Room _room = (Room)_evt.Params["room"];

		Debug.Log("[" + this.name + "] [" + this.GetType().Name + "] User " + _user + " hat den Raum " + _room + " verlassen.");
	}
	
	// ----------------------------------------------------
	private void OnPublicMessage(BaseEvent _evt) {
		string _message = (string)_evt.Params["message"];
		User _sender = (User)_evt.Params["sender"];
		
//		VerGui_Work.ThisInstance.ChatText = "Message: " + _message + ", Sender: " + _sender.Name;

		Debug.Log("[" + this.name + "] [" + this.GetType().Name + "] Message: " + _message + ", Sender: " + _sender.Name);
	}



	
	/* ====================================================
	 * Eventhandler
	 * 
	 * Ab hier kommen die ganzen Handler um auf die Events,
	 * die der Server uns schickt, zu reagieren. Die
	 * Eventhandler werden von den SFS Callbacks aus 
	 * aufgerufen (siehe oben)
	 * 
	 * 
	 * ====================================================
	 */

	// ----------------------------------------------------
	private void HandleSpawnChar(ISFSObject _params) {
		
		ISFSObject _charData = _params.GetSFSObject("char");
		
		int _charID = _charData.GetInt("id");
		
		Debug.Log("Habe eine SpawnChar Message vom Server erhalten. Char ID: " + _charID);

		if(_charID == sfs.MySelf.Id) {
			
			// Ich habe eben die Spawn Nachricht für mich selber erhalten
			Debug.Log("Die SpawnChar Message war ich selber. Meine User ID: " + sfs.MySelf.Id);
		}
		else {
			
			// Die Spawn Nachricht betraf einen anderen Spieler
			Debug.Log("Die SpawnChar Message betraf einen anderen! Den zeige ich nun an!");
			GameObject _charObj = Instantiate(Resources.Load(settingDefaultOtherCharPrefab)) as GameObject;
			_charObj.transform.position = new Vector3(-2.2f, -0.05f, 0.11f);
		}
	}

	// ----------------------------------------------------
	private void HandleReceiveChatMessage(ISFSObject _params) {
		
//		ISFSObject _playerData = _params.GetSFSObject("player");
	}

	

	
	/* ====================================================
	 * Services des NwM
	 * 
	 * Hier stehen alle Methoden die andere Instanzen im Game
	 * nutzen können, wenn sie was vom Server wollen.
	 * Grundsätzlich wird mit dem SFS Server nur über
	 * den NwM hier kommuniziert, niemals direkt!
	 * Ausnahmen: - Startup-Sequenz (Connection einrichten)
	 *            - Login GUI (bevor es einen Char gibt)
	 * 
	 * 
	 * ====================================================
	 */

	// ----------------------------------------------------
	// Wenn alles eingerichtet ist, bitten wir den Server darum, dass die Spielfigur gespawned wird
	// ----------------------------------------------------
	public void SendSpawnRequest() {
		ISFSObject _obj = new SFSObject();
		Room _room = sfs.LastJoinedRoom;
		
		ExtensionRequest _req = new ExtensionRequest("spawnMe", _obj, _room);
		sfs.Send(_req);
		
		Debug.Log("[" + this.name + "] [" + this.GetType().Name + "] Spawn Request for Scene " + Application.loadedLevelName + " sent to server");
	}
	
	// ----------------------------------------------------
	// Transform Daten an den Server schicken
	// ----------------------------------------------------
	public void SendTransform() {
		ISFSObject _obj = new SFSObject();
		Room _room = sfs.LastJoinedRoom;
		
		ExtensionRequest _req = new ExtensionRequest("sendTransform", _obj, _room);
		sfs.Send(_req);
		
		Debug.Log("[" + this.name + "] [" + this.GetType().Name + "] Spawn Request for Scene " + Application.loadedLevelName + " sent to server");
	}
	
	// ----------------------------------------------------
	// Chat Message an alle raussenden
	// ----------------------------------------------------
	public void SendChatMessage(string _message) {
		Debug.Log("[" + this.name + "] [" + this.GetType().Name + "] Chat Message in Scene " + Application.loadedLevelName + " sent to server");

//		ISFSObject _obj = new SFSObject();
//		ExtensionRequest _req = new ExtensionRequest("chatMessage", _obj);
//		sfs.Send(_req);
		
		sfs.Send(new PublicMessageRequest(_message));

	}

	

	
	/* ====================================================
	 * Sonstige Hilfsmethoden
	 * 
	 * 
	 * 
	 * 
	 * ====================================================
	 */
	
	// ------------------------------------------------------------------------
	// Vor einem Scene-Wechsel alle Callbacks removen. Die neue Scene richtet ihre eigenen ein
	void UnregisterSFSSceneCallbacks() {
		Debug.Log("[" + this.name + "] [" + this.GetType().Name + "] Alle Callbacks der Scene " + Application.loadedLevelName + " werden zurueckgesetzt");
		sfs.RemoveAllEventListeners();
	}
	
	// ------------------------------------------------------------------------
	// Die Applikation kontrolliert beenden
	public void ApplicationQuit() {
		
		if (sfs != null && sfs.IsConnected) {
			Debug.Log("[" + this.name + "] [" + this.GetType().Name + "] Connection wird beendet");
			sfs.Disconnect();
		}
		else {
			if (sfs != null && !sfs.IsConnected) {
				Debug.Log("[" + this.name + "] [" + this.GetType().Name + "] Connection ist bereits beendet");
			}
		}
	}
	
}
