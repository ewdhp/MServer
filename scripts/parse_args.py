import argparse

def parse_inputs():
    parser = argparse.ArgumentParser()
    parser.add_argument('--host', type=str, required=False)
    parser.add_argument('--username', type=str, required=False)
    parser.add_argument('--password', type=str, required=False)
    # Add more arguments as needed
    return parser.parse_args()

# Example usage:
# args = parse_inputs()
# print(args.host, args.username, args.password)
