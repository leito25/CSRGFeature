using UnityEngine;

public class SinLoop : MonoBehaviour
{
    public float amplitude = 1.0f;  // The amplitude of the sine wave
    public float frequency = 1.0f;  // The frequency of the sine wave
    public Vector3 startPosition;
    public GameObject targetObj;

    // Start is called before the first frame update
    void Start()
    {
        // Store the starting position of the camera
        startPosition = transform.position;
    }

    // Update is called once per frame
    void Update()
    {
        // Calculate the new Y position using a sine wave
        float newY = startPosition.y + Mathf.Sin(Time.time * frequency) * amplitude;

        // Update the camera's position
        transform.position = new Vector3(startPosition.x, startPosition.y, newY);
    }
}
