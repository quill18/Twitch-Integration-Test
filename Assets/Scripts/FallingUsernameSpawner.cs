using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class FallingUsernameSpawner : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        QuillsAwesomeChat.ConnectionManager cm = GameObject.FindAnyObjectByType<QuillsAwesomeChat.ConnectionManager>();

        if (cm == null)
        {
            Debug.LogError("Could not find an instance of QuillsAwesomeChat.ConnectionManager");
            return;
        }

        cm.ChatMessageListeners += ChatMessageListener;

        rectTransform = this.GetComponent<RectTransform>();
    }

    public GameObject FallingUsernamePrefab;
    private RectTransform rectTransform;

    void ChatMessageListener(string username, string message)
    {
        Debug.Log("ChatMessageListener: " + username);

        float width = rectTransform.rect.width + rectTransform.sizeDelta.x;

        Vector3 randomX = new Vector3( Random.Range(-width/2f, width/2f), 0, 0 );

        GameObject go = Instantiate(FallingUsernamePrefab, this.transform.position + randomX, Quaternion.identity, this.transform );

        TMP_Text t = go.GetComponentInChildren<TMP_Text>();

        if (t == null)
        {
            Debug.LogError("Falling Username Prefab has no TextMeshPro");
            return;
        }

        t.text = username;
        
        t.color = Random.ColorHSV(0f, 1f);
    }
}
