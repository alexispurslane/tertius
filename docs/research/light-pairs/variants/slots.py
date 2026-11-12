"""Scratch edit of ticket t2-mlvy, variant "fixed slots": the corner pass of key and fill, version 2, keeps its greatest
losses in five named values with a fixed chain of compare and carry, in place of two local arrays that it indexed with a
run-time index. The chosen keys and shares are the same. Usage: slots.py <root of a copy of the tree>"""
import sys

p = f"{sys.argv[1]}/src/Tertius.Rendering/Shaders/key_fill_corners.frag"
s = open(p).read()

def swap(a, b):
    global s
    assert s.count(a) == 1, a[:70]
    s = s.replace(a, b)

swap("""void main()
{
    outKeys = uvec4(NoKey);
""", """// Keeps the greater of the slot's loss and `loss` in the slot, with its key, and carries the lesser on in `loss` and
// `key`. A chain of these over the slots, greatest first, keeps the slots in the order of loss.
void sift(inout float slotLoss, inout uint slotKey, inout float loss, inout uint key)
{
    bool greater = loss > slotLoss;
    float lesserLoss = greater ? slotLoss : loss;
    uint lesserKey = greater ? slotKey : key;
    slotLoss = greater ? loss : slotLoss;
    slotKey = greater ? key : slotKey;
    loss = lesserLoss;
    key = lesserKey;
}

// The key of a slot with its share in the key's 8 bits, as the direct pass reads it. NoKey for a slot past the key
// lights, or one whose loss earns no share.
uint sharedKey(uint key, float loss, float threshold, bool isKey)
{
    uint share = uint(keyShare(loss, threshold) * 255.0 + 0.5);
    return isKey && share > 0u ? key | (share << 16) : NoKey;
}

void main()
{
    outKeys = uvec4(NoKey);
""")
swap("""    // The keyLights + 1 greatest losses, greatest first, each with its light's key without a share.
    float greatest[5] = float[5](0.0, 0.0, 0.0, 0.0, 0.0);
    uint greatestKey[5] = uint[5](NoKey, NoKey, NoKey, NoKey, NoKey);
""", """    // The five greatest losses, greatest first, each with its light's key without a share: one more than the most key
    // lights. They are named values and not an array, because an array that a run-time index reads lives in memory and
    // not in registers on a tiled GPU.
    float loss0 = 0.0, loss1 = 0.0, loss2 = 0.0, loss3 = 0.0, loss4 = 0.0;
    uint key0 = NoKey, key1 = NoKey, key2 = NoKey, key3 = NoKey, key4 = NoKey;
""")
swap("""        for (uint k = 0u; k <= keyLights; k++)
            if (loss > greatest[k])
            {
                float lowerLoss = greatest[k];
                uint lowerKey = greatestKey[k];
                greatest[k] = loss;
                greatestKey[k] = key;
                loss = lowerLoss;
                key = lowerKey;
            }
""", """        sift(loss0, key0, loss, key);
        sift(loss1, key1, loss, key);
        sift(loss2, key2, loss, key);
        sift(loss3, key3, loss, key);
        sift(loss4, key4, loss, key);
""")
swap("""    float threshold = max(greatest[keyLights], NegligibleLoss * whole);
    uvec4 keys = uvec4(NoKey);
    uint slot = 0u;
    for (uint k = 0u; k < keyLights; k++)
    {
        // In the key's 8 bits, as the direct pass reads it.
        uint share = uint(keyShare(greatest[k], threshold) * 255.0 + 0.5);
        if (share > 0u)
            keys[slot++] = greatestKey[k] | (share << 16);
    }
    outKeys = keys;
""", """    // The loss of the light that follows the key lights.
    float next = keyLights == 0u ? loss0 : keyLights == 1u ? loss1 : keyLights == 2u ? loss2 : keyLights == 3u ? loss3 : loss4;
    float threshold = max(next, NegligibleLoss * whole);
    // The shares fall with the losses, so the slots with a share come first and the keys need no closing up.
    outKeys = uvec4(
        sharedKey(key0, loss0, threshold, keyLights > 0u),
        sharedKey(key1, loss1, threshold, keyLights > 1u),
        sharedKey(key2, loss2, threshold, keyLights > 2u),
        sharedKey(key3, loss3, threshold, keyLights > 3u)
    );
""")
open(p, "w").write(s)
