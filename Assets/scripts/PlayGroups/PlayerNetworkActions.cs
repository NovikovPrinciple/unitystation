﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using Events;
using PlayGroup;
using Equipment;
using Cupboards;
using UI;
using Items;
using System.Linq;
using UnityEngine.Assertions.Must;

public partial class PlayerNetworkActions : NetworkBehaviour
{
    private Dictionary<string, GameObject> _inventory = new Dictionary<string, GameObject>();

    private string[] slotNames = {
        "suit", "belt", "feet", "head", "mask", "uniform", "neck", "ear", "eyes", "hands",
        "id", "back", "rightHand", "leftHand", "storage01", "storage02", "suitStorage"
    };

    private Equipment.Equipment equipment;
    private PlayerMove playerMove;
    private PlayerSprites playerSprites;
    private PlayerScript playerScript;
    private SoundNetworkActions soundNetworkActions;
    private ChatIcon chatIcon;

    void Start()
    {
        equipment = GetComponent<Equipment.Equipment>();
        playerMove = GetComponent<PlayerMove>();
        playerSprites = GetComponent<PlayerSprites>();
        playerScript = GetComponent<PlayerScript>();
        soundNetworkActions = GetComponent<SoundNetworkActions>();
        chatIcon = GetComponentInChildren<ChatIcon>();
    }

    public override void OnStartServer()
    {
		if (isServer) {
			foreach (string slotName in slotNames) {
				_inventory.Add(slotName, null);
			}
		} else {
			CmdSyncRoundTime(GameManager.Instance.GetRoundTime);
		}
        base.OnStartServer();
    }

    public Dictionary<string, GameObject> Inventory
    {
        get { return _inventory; }
    }

    [Server]
    public bool AddItem(GameObject itemObject, string slotName = null, bool replaceIfOccupied = false, bool forceInform = true)
    {
        var eventName = slotName ?? UIManager.Hands.CurrentSlot.eventName;
        if ( _inventory[eventName] != null && _inventory[eventName] != itemObject && !replaceIfOccupied )
        {
            Debug.LogFormat("{0}: Didn't replace existing {1} item {2} with {3}",
                gameObject.name, eventName, _inventory[eventName].name, itemObject.name);
            return false;
        }
        EquipmentPool.AddGameObject(gameObject, itemObject);
        SetInventorySlot(slotName, itemObject);
        UpdateSlotMessage.Send(gameObject, eventName, itemObject, forceInform);
        return true;
    }
    void PlaceInHand(GameObject item)
    {
        UIManager.Hands.CurrentSlot.SetItem(item);
    }
    //This is for objects that aren't picked up via the hand (I.E a magazine clip inside a weapon that was picked up)
    //TODO make these private(make some public child-aware high level methods instead):
    [Server]
    public void RemoveFromEquipmentPool(GameObject obj)
    {
        EquipmentPool.DropGameObject(gameObject, obj);
    }
    [Server]
    public void AddToEquipmentPool(GameObject obj)
    {
        EquipmentPool.AddGameObject(gameObject, obj);
    }

    [Server]
    public bool ValidateInvInteraction(string slot, GameObject gObj = null, bool forceClientInform = true)
    {
        if ( !_inventory[slot] && gObj && _inventory.ContainsValue(gObj) )
        {
            UpdateSlotMessage.Send(gameObject, slot, gObj, forceClientInform);
            SetInventorySlot(slot, gObj);
            //Clean up other slots
            ClearObjectIfNotInSlot(gObj, slot, forceClientInform);
//            Debug.LogFormat("Approved moving {0} to slot {1}", gObj, slot);
            return true;
        }
        if ( !gObj )
        {
            return ValidateDropItem(slot, forceClientInform);
        }
        Debug.LogWarningFormat("Unable to validateInvInteraction {0}:{1}", slot, gObj.name);
        return false;
    }

    public void RollbackPrediction(string slot)
    {
        //todo fix
        /*
        KeyNotFoundException: The given key was not present in the dictionary.
        System.Collections.Generic.Dictionary`2[System.String,UnityEngine.GameObject].get_Item (System.String key) 
            (at /Users/builduser/buildslave/mono/build/mcs/class/corlib/System.Collections.Generic/Dictionary.cs:150)
        PlayerNetworkActions.RollbackPrediction (System.String slot) (at Assets/Scripts/PlayGroups/PlayerNetworkActions.cs:113)
        TableTrigger.Interact (UnityEngine.GameObject originator, System.String hand) (at Assets/Scripts/Items/TableTrigger.cs:20)
        InputControl.InputTrigger.Interact () (at Assets/Scripts/PlayGroups/Input/InputTrigger.cs:19)
        InputControl.InputTrigger.Trigger () (at Assets/Scripts/PlayGroups/Input/InputTrigger.cs:14)
        InputControl.InputController.Interact (UnityEngine.Transform transform) (at Assets/Scripts/PlayGroups/Input/InputController.cs:186)
        */
        UpdateSlotMessage.Send(gameObject, slot, _inventory[slot], true);
    }

    [Server]
    private void ClearObjectIfNotInSlot(GameObject gObj, string slot, bool forceClientInform)
    {
        HashSet<string> toBeCleared = new HashSet<string>();
        foreach (string key in _inventory.Keys)
        {
            if (key.Equals(slot) || !_inventory[key]) continue;
            if (_inventory[key].Equals(gObj))
            {
                toBeCleared.Add(key);
            }
        }
        ClearInventorySlot(forceClientInform, toBeCleared.ToArray());
    }

    [Server]
    public void ClearInventorySlot(params string[] slotNames)
    {
        ClearInventorySlot(true, slotNames);
    }

    [Server]
    private void ClearInventorySlot(bool forceClientInform, params string[] slotNames)
    {
        for ( int i = 0; i < slotNames.Length; i++ )
        {
            _inventory[slotNames[i]] = null;
            if (slotNames[i] == "id" || slotNames[i] == "storage01" 
                || slotNames[i] == "storage02" || slotNames[i] == "suitStorage")
            {
                //Not clearing onPlayer sprites for these as they don't have any
            }
            else
            {
                equipment.ClearItemSprite(slotNames[i]);
            }
                UpdateSlotMessage.Send(gameObject, slotNames[i], null, forceClientInform);
        }
//        Debug.LogFormat("Cleared {0}", slotNames);
    }

    [Server]
    public void SetInventorySlot(string slotName, GameObject obj)
    {
        _inventory[slotName] = obj;
        ItemAttributes att = obj.GetComponent<ItemAttributes>();
        if (slotName == "leftHand" || slotName == "rightHand")
        {
            equipment.SetHandItemSprite(slotName, att);
        }
        else
        {
            if (slotName == "id" || slotName == "storage01" 
                || slotName == "storage02" || slotName == "suitStorage")
            {
                //Not setting onPlayer sprites for these as they don't have any
            }
            else
            {
                if (att.spriteType == SpriteType.Clothing)
                {
                    // Debug.Log("slotName = " + slotName);
                    Epos enumA = (Epos)Enum.Parse(typeof(Epos), slotName);
                    equipment.syncEquipSprites[(int)enumA] = att.clothingReference;
                }
            }
        }
    }
    [Command]
    [Obsolete]
    public void CmdTryToInstantiateInHand(string eventName, GameObject prefab)
    {
        if ( _inventory.ContainsKey(eventName) )
        {
            if ( _inventory[eventName] == null )
            {
                GameObject item = Instantiate(prefab, Vector3.zero, Quaternion.identity) as GameObject;
                NetworkServer.Spawn(item);
                EquipmentPool.AddGameObject(gameObject, item);
                _inventory[eventName] = item;
                equipment.SetHandItem(eventName, item);
                RpcInstantiateInHand(gameObject.name, item);
            }
            else
            {
                Debug.Log("Inventory slot is full");

            }
        }
    }

    [ClientRpc]
    [Obsolete]
    void RpcInstantiateInHand(string playerName, GameObject item)
    {
        if ( playerName == gameObject.name )
        {
            UIManager.Hands.CurrentSlot.TrySetItem(item);
        }
    }

    /// Drop an item from a slot. use forceSlotUpdate=false when doing clientside prediction, 
    /// otherwise client will forcefully receive update slot messages
    public void DropItem(string hand, bool forceClientInform = true)
    {
        InventoryInteractMessage.Send(hand, null, forceClientInform);
    }

    //Dropping from a slot on the UI
    [Server]
    public bool ValidateDropItem(string slot, bool forceClientInform/* = false*/)
    {
        //decline if not dropped from hands?
        if ( _inventory.ContainsKey(slot) && _inventory[slot] )
        {
            EquipmentPool.DropGameObject(gameObject, _inventory[slot]);

//            RpcAdjustItemParent(_inventory[slot], null);
            _inventory[slot] = null;
            equipment.ClearItemSprite(slot);
            UpdateSlotMessage.Send(gameObject, slot, null, forceClientInform);
            return true;
        }
        Debug.Log("Object not found in Inventory");
        return false;
    }

    //Dropping from somewhere else in the players equipmentpool (Magazine ejects from weapons etc)
    [Command][Obsolete]
    public void CmdDropItemNotInUISlot(GameObject obj)
    {
        EquipmentPool.DropGameObject(gameObject, obj);
    }

    public void DisposeOfChildItem(GameObject obj)
    {
        EquipmentPool.DisposeOfObject(gameObject, obj);
    }

    [Server]
    public void PlaceItem(string slotName, Vector3 pos, GameObject newParent)
    {
        if ( !SlotNotEmpty(slotName) ) return;
        GameObject item = _inventory[slotName];
        EquipmentPool.DropGameObject(gameObject, _inventory[slotName], pos);
        ClearInventorySlot(slotName);
        if (item != null && newParent != null)
        {
            item.transform.parent = newParent.transform;
            World.ReorderGameobjectsOnTile(pos);
        }
//        RpcAdjustItemParent(item, newParent);
        
        
        
//        if ( !SlotNotEmpty(slotName) ) return;
//        GameObject item = _inventory[slotName];
//        EquipmentPool.DropGameObject(gameObject, _inventory[slotName], pos);
//        _inventory[slotName] = null;
//        if (item != null && newParent != null)
//        {
//            item.transform.parent = newParent.transform;
//            World.ReorderGameobjectsOnTile(pos);
//        }
//        RpcAdjustItemParent(item, newParent);
//        equipment.ClearItemSprite(slotName);
    }

    public bool SlotNotEmpty(string eventName)
    {
        return _inventory.ContainsKey(eventName) && _inventory[eventName] != null;
    }

    [Command]
    public void CmdToggleCupboard(GameObject cupbObj)
    {
        ClosetControl closetControl = cupbObj.GetComponent<ClosetControl>();
        closetControl.ServerToggleCupboard();
    }

 

    [Command]
    public void CmdStartMicrowave(GameObject microwave, string mealName)
    {
        Microwave m = microwave.GetComponent<Microwave>();
        m.ServerSetOutputMeal(mealName);
        m.RpcStartCooking();
    }

    [Command]
    public void CmdRequestJob(JobType jobType)
    {
        // Already have a job buddy!
        if (playerScript.JobType != JobType.NULL)
            return;

        playerScript.JobType = GameManager.Instance.GetRandomFreeOccupation(jobType);
        StartCoroutine(equipment.SetPlayerLoadOuts());
    }

    [Command]
    public void CmdToggleShutters(GameObject switchObj)
    {
        ShutterSwitchTrigger s = switchObj.GetComponent<ShutterSwitchTrigger>();
        if (s.IsClosed)
        {
            s.IsClosed = false;
        }
        else
        {
            s.IsClosed = true;
        }
    }

    [Command]
    public void CmdToggleLightSwitch(GameObject switchObj)
    {
        Lighting.LightSwitchTrigger s = switchObj.GetComponent<Lighting.LightSwitchTrigger>();
        s.isOn = !s.isOn;
    }

    [Command]
    public void CmdToggleFireCabinet(GameObject cabObj, bool forItemInteract)
    {
        CabinetTrigger c = cabObj.GetComponent<CabinetTrigger>();

        if (!forItemInteract)
        {
            if (c.IsClosed)
            {
                c.IsClosed = false;
            }
            else
            {
                c.IsClosed = true;
            }
        }
        else
        {
            Debug.Log("TODO: condition to place extinguisher back");
			c.isFull = false;
        }
    }

    [Command]
    public void CmdMoveItem(GameObject item, Vector3 newPos)
    {
        item.transform.position = newPos;
    }

    [Command]
    public void CmdConsciousState(bool conscious)
    {
        if (conscious)
        {
            playerMove.allowInput = true;
            RpcSetPlayerRot(false, 0f);
        }
        else
        {
            playerMove.allowInput = false;
            RpcSetPlayerRot(false, -90f);
            soundNetworkActions.RpcPlayNetworkSound("Bodyfall", transform.position);
            if (UnityEngine.Random.value > 0.5f)
            {
                playerSprites.currentDirection = Vector2.up;
            }
        }
    }

    [Command]
    public void CmdSendChatMessage(string msg, bool isLocalChat)
    {
        if (isLocalChat)
        {
            //regex to sanitise any injected html tags
            var rx = new Regex("[<][^>]+[>]");
            var inputString = rx.Replace(msg, "");

            //might as well use it here so it doesn't matter how long the input string is
            rx = new Regex("^(/me )");
            if (rx.IsMatch(inputString))
            { // /me message
                inputString = rx.Replace(inputString, " ");
                ChatRelay.Instance.chatlog.Add(new ChatEvent("<i><b>" + gameObject.name + "</b>" + inputString + "</i>."));
            }
            else
            { // chat message
                ChatRelay.Instance.chatlog.Add(new ChatEvent("<b>" + gameObject.name + "</b>" + " says, " + "\"" + inputString + "\""));
            }
        }

    }


    [Command]
    //send a generic message
    public void CmdSendAlertMessage(string msg, bool isLocalChat)
    {
        if (isLocalChat)
        {
            ChatRelay.Instance.chatlog.Add(new ChatEvent(msg));
        }
    }

    [Command]
    public void CmdToggleChatIcon(bool turnOn)
    {
        RpcToggleChatIcon(turnOn);
    }

    [ClientRpc]
    void RpcToggleChatIcon(bool turnOn)
    {
        if (turnOn)
        {
            chatIcon.TurnOnTalkIcon();
        }
        else
        {
            chatIcon.TurnOffTalkIcon();
        }
    }

    //For falling over and getting back up again over network

    [ClientRpc]
    public void RpcSetPlayerRot(bool temporary, float rot)
    {
//		Debug.LogWarning("Setting TileType to none for player and adjusting sortlayers in RpcSetPlayerRot");
		SpriteRenderer[] spriteRends = GetComponentsInChildren<SpriteRenderer>();
		foreach (SpriteRenderer sR in spriteRends) {
			sR.sortingLayerName = "Blood";
		}
		gameObject.GetComponent<Matrix.RegisterTile>().UpdateTileType(Matrix.TileType.None);
        var rotationVector = transform.rotation.eulerAngles;
        rotationVector.z = rot;
        transform.rotation = Quaternion.Euler(rotationVector);
        //So other players can walk over the Unconscious
        playerSprites.AdjustSpriteOrders(-30);
        if (temporary)
        {
            //TODO Coroutine with timer to get back up again
        }
    }

//    [ClientRpc]
//    void RpcAdjustItemParent(GameObject item, GameObject parent)
//    {
//        if (parent != null)
//        {
//            item.transform.parent = parent.transform;
//        }
//        else
//        {
//            item.transform.parent = null;
//        }
//    }
//
//    [ClientRpc]
//    void RpcAdjustItemParentCupB(GameObject item, GameObject parent)
//    {
//        if (parent != null)
//        {
//            ClosetControl closetCtrl = parent.GetComponent<ClosetControl>();
//            item.transform.parent = closetCtrl.items.transform;
//        }
//        else
//        {
//            item.transform.parent = null;
//        }
//    }

    [ClientRpc]
    public void RpcSpawnGhost()
    {
        playerScript.ghost.SetActive(true);
        playerScript.ghost.transform.parent = null;
        chatIcon.gameObject.transform.parent = playerScript.ghost.transform;
        playerScript.ghost.transform.rotation = Quaternion.identity;
        if (PlayerManager.LocalPlayer == gameObject)
        {
            SoundManager.Stop("Critstate");
            Camera2DFollow.followControl.target = playerScript.ghost.transform;
            var fovScript = GetComponent<FieldOfView>();
            if (fovScript != null)
                fovScript.enabled = false;
        }
    }


    //Respawn action for Deathmatch v 0.1.3

    [Server]
	public void RespawnPlayer(int timeout = 0)
    {
        StartCoroutine(initiateRespawn(timeout));
    }
    
    [Server]
    private IEnumerator initiateRespawn(int timeout)
    {
        Debug.LogFormat("{0}: Initiated respawn in {1}s", gameObject.name, timeout);
        yield return new WaitForSeconds(timeout);
        RpcAdjustForRespawn();
        var spawn = CustomNetworkManager.Instance.GetStartPosition();
        var newPlayer =
            Instantiate(CustomNetworkManager.Instance.playerPrefab, spawn.position, spawn.rotation);
//		NetworkServer.Destroy( this.gameObject );
        EquipmentPool.ClearPool(gameObject.name);
        PlayerList.Instance.connectedPlayers[gameObject.name] = newPlayer;
        NetworkServer.ReplacePlayerForConnection(connectionToClient, newPlayer, playerControllerId);
    }

    [ClientRpc]
	private void RpcAdjustForRespawn(){
			playerScript.ghost.SetActive(false);
			gameObject.GetComponent<InputControl.InputController>().enabled = false;
	}

    [Command]
    void CmdSyncRoundTime(float currentTime)
    {
        RpcSyncRoundTime(currentTime);
    }

    [ClientRpc]
    void RpcSyncRoundTime(float currentTime)
    {
        if ( PlayerManager.LocalPlayer == gameObject )
        {
            GameManager.Instance.SyncTime(currentTime);
        }
    }

    [Command]
    public void CmdTryOpenRestrictDoor(GameObject door){
        door.GetComponent<DoorController>().CmdTryOpen(gameObject);
    }
    [Command]
    public void CmdRestrictDoorDenied(GameObject door){
        door.GetComponent<DoorController>().CmdTryDenied();
    }
}