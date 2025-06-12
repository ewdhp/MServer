# ...all unique imports...
import numpy as np
import json
# ...other imports...

def node1(input_data):
    # ...node 1 logic...
    output1 = input_data + 1
    return output1

def node2(input_data):
    # ...node 2 logic...
    output2 = input_data * 2
    return output2

def node3(input_data):
    # ...node 3 logic...
    output3 = np.sqrt(input_data)
    return output3

def main():
    data = 10
    out1 = node1(data)
    out2 = node2(out1)
    out3 = node3(out2)
    print("Final output:", out3)

if __name__ == "__main__":
    main()
