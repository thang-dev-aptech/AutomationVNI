import React, { useState } from 'react'
import CustomerList from './components/CustomerList'
import CustomerProfile from './components/CustomerProfile'

export const CustomersFeature = () => {
  const [selectedCustomerId, setSelectedCustomerId] = useState(null)

  return (
    <div>
      {selectedCustomerId ? (
        <CustomerProfile
          customerId={selectedCustomerId}
          onBack={() => setSelectedCustomerId(null)}
          onCustomerUpdated={() => {}}
        />
      ) : (
        <CustomerList onSelectCustomer={(id) => setSelectedCustomerId(id)} />
      )}
    </div>
  )
}

export default CustomersFeature
