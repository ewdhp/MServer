#!/bin/bash

while [[ $# -gt 0 ]]
do
    key="$1"
    case $key in
        --*)
        varname="${key/--/}"
        declare "$varname"="$2"
        shift # past argument
        shift # past value
        ;;
        *)
        shift # past unknown arg
        ;;
    esac
done

# Example usage:
# echo "Value of foo: $foo"
