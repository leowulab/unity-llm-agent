using UnityEngine;
using GridGame.Core;

public class KeyboardInput : MonoBehaviour
{
    [SerializeField] private GridView view;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.UpArrow)) view.DoMove(Direction.Up);
        if (Input.GetKeyDown(KeyCode.DownArrow)) view.DoMove(Direction.Down);
        if (Input.GetKeyDown(KeyCode.LeftArrow)) view.DoMove(Direction.Left);
        if (Input.GetKeyDown(KeyCode.RightArrow)) view.DoMove(Direction.Right);
        if (Input.GetKeyDown(KeyCode.R)) view.DoReset(Random.Range(0, 99999));
        if (Input.GetKeyDown(KeyCode.O)) view.DebugPrintObservation();
    }
}