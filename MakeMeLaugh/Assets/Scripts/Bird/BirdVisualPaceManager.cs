using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MakeMeLaugh.Assets.Scripts.Bird;

public class BirdVisualPaceManager : MonoBehaviour
{
    // NOTE: Float from 0.0 to 1.0 depending on how progressed the game is.
    [SerializeField]
    private float currentProgress;

    [SerializeField]
    private int startingBirdCount = 0;

    [SerializeField]
    private int finalMaxBirdCount = 50;

    public BirdManager birdManager;
    
    // Start is called before the first frame update
    void Start()
    {
        for (int i = 0; i < startingBirdCount; i++)
        {
            birdManager.AddBird();
        }
    }

    public void CheckBirdCount()
    {
        int birdCountDelta = finalMaxBirdCount - startingBirdCount; 
        float expectedBirdCount = startingBirdCount + birdCountDelta * currentProgress;
        int currentBirdCount = birdManager.BirdCount;
        if (currentBirdCount < expectedBirdCount)
        {
            for (int i = currentBirdCount; i < expectedBirdCount; i++)
            {
                birdManager.AddBird();
            }
        }
    }

    public void ResetProgress()
    {
        currentProgress = 0;
    }

    public void UpdateBirdProgression(float progress)
    {
        if (progress > currentProgress)
        {
            currentProgress = progress;
            CheckBirdCount();
        }
    }
}
