using UnityEngine;

public class Score : MonoBehaviour
{
    private static int score = 0;

    public static void AddScore(int amount)
    {
        score += amount;
        Debug.Log("Score: " + score);
    }

    public static int GetScore()
    {
        return score;
    }

    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
